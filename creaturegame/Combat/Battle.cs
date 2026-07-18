using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

public class Battle
{
    // The active player creature. Reassignable because a forced faint-switch (Phase 4 Stage 3) can bring in a
    // bench member mid-battle when this one faints — every turn-loop read below follows the active creature.
    private Creature PlayerCreature { get; set; }
    private Creature EnemyCreature { get; }
    private readonly ITypeChart _typeChart;
    private readonly IBattleRules _rules;
    private readonly IBattleInput _playerInput;
    private readonly IBattleInput _enemyInput;
    private readonly IBattleEventEmitter? _emitter;
    private readonly IReadOnlyList<Attack> _movePool;
    private readonly IRandomSource _rng;
    private readonly CarriedStatus? _playerEntryStatus;
    private readonly Bag? _playerBag;
    private readonly bool _escapable;
    private readonly bool _trainerBattle;
    private readonly RunRules _runRules;

    // The player's party, threaded from the run loop when the battle is party-aware (Phase 4 Stage 3). When set,
    // a faint of the active creature that leaves a live bench member triggers a forced switch-in instead of ending
    // the battle; null keeps the legacy single-creature behaviour (a faint ends the battle), so every direct
    // Battle caller (tests, the endless chain) is unchanged.
    private readonly Party? _playerParty;
    private int _turnNumber;

    /// <summary>
    /// True if this battle ended because a side fled (Roar/Whirlwind in a wild battle) rather than fainting.
    /// The run loop reads it to advance the encounter without a win/loss or XP. False until then.
    /// </summary>
    public bool EndedInFlee { get; private set; }

    public Battle(
        Creature player,
        Creature enemy,
        ITypeChart typeChart,
        IBattleInput playerInput,
        IBattleInput enemyInput,
        IReadOnlyList<Attack>? movePool = null,
        IBattleRules? rules = null,
        IBattleEventEmitter? emitter = null,
        IRandomSource? rng = null,
        CarriedStatus? playerEntryStatus = null,
        Bag? playerBag = null,
        bool escapable = true,
        // True when the defeated foe is trainer-owned (the Elite/Boss "trainer-analog" tiers) — the run layer
        // supplies this fact and the Gen-1 seam applies the trainer ×1.5 XP bonus. False (default) = a wild foe,
        // so every direct Battle caller stays on pure wild XP.
        bool trainerBattle = false,
        // Roguelite run-balance rules applied on top of the Gen-1 seam (see the XP award site in the turn loop).
        // NOT a generation seam: the Gen-1 formula stays pure in IBattleRules.CalculateXpAwarded; RunRules only
        // scales its result so the run layer can tune levelling pace without touching Gen-1 fidelity. Null →
        // RunRules.Default (a 1.0 no-op), so every direct Battle caller (tests, the legacy chain) is unchanged.
        RunRules? runRules = null,
        // The player's party (Phase 4 Stage 3, forced-switch-on-faint). When supplied, `player` must be its
        // current Lead; a faint of the active creature that leaves a live bench member sends in a replacement
        // against the same enemy instead of ending the battle. Null (the default) = the legacy single-creature
        // battle (a faint ends it), so every existing Battle caller is unchanged.
        Party? playerParty = null
    )
    {
        PlayerCreature = player;
        EnemyCreature = enemy;
        _typeChart = typeChart;
        _rules = rules ?? Gen1BattleRules.Instance;
        _playerInput = playerInput;
        _enemyInput = enemyInput;
        _movePool = movePool ?? Array.Empty<Attack>();
        _emitter = emitter;
        _rng = rng ?? SystemRandomSource.Instance;
        _playerEntryStatus = playerEntryStatus;
        _playerBag = playerBag;
        _escapable = escapable;
        _trainerBattle = trainerBattle;
        _runRules = runRules ?? RunRules.Default;
        _playerParty = playerParty;
    }

    public async Task StartFightAsync()
    {
        PlayerCreature.ResetBattleState();
        EnemyCreature.ResetBattleState();

        // A player carried over from a previous encounter in an endless run keeps its major status — Gen 1
        // persists status out of battle, but the per-battle reset above just cleared it, so re-apply.
        // Volatiles (confusion, stat stages, …) are deliberately NOT carried. Enemies are always freshly
        // built, so they never carry anything.
        ApplyEntryStatus(_playerEntryStatus);

        _emitter?.Emit(
            new BattleStarted(
                PlayerCreature.Name,
                EnemyCreature.Name,
                EnemyCreature.SpeciesId,
                EnemyCreature.Level
            )
        );

        while (PlayerCreature.IsAlive() && EnemyCreature.IsAlive())
        {
            _turnNumber++;

            _emitter?.Emit(
                new TurnStarted(
                    _turnNumber,
                    PlayerCreature.Name,
                    PlayerCreature.Attributes.HP,
                    PlayerCreature.Attributes.MaxHP,
                    PlayerCreature.Battle.Status,
                    PlayerCreature.XpThisLevel,
                    PlayerCreature.XpToNextLevel,
                    EnemyCreature.Name,
                    EnemyCreature.Attributes.HP,
                    EnemyCreature.Attributes.MaxHP,
                    EnemyCreature.Battle.Status,
                    PlayerCreature
                        .MoveSet.Select(m => new MoveInfo(
                            m.Base.Name ?? "",
                            m.Base.DamageType,
                            m.PowerPointsCurrent,
                            m.Base.PowerPointsMax,
                            m == PlayerCreature.Battle.DisabledMove,
                            // STAB: a damaging move whose type matches the user's current type (mirrors the
                            // DamageCalculator condition). Fixed-damage moves (BaseDamage 0) get no STAB.
                            m.Base.BaseDamage > 0
                                && (
                                    PlayerCreature.Type1 == m.Base.DamageType
                                    || PlayerCreature.Type2 == m.Base.DamageType
                                ),
                            // Type effectiveness vs the live enemy, via the active type chart — damaging moves
                            // only (fixed-damage/status moves ignore the chart, so they report neutral 1.0).
                            m.Base.BaseDamage > 0
                                ? DamageCalculator.GetTypeEffectiveness(
                                    m.Base.DamageType,
                                    EnemyCreature.Type1,
                                    EnemyCreature.Type2,
                                    _typeChart
                                )
                                : 1.0
                        ))
                        .ToList()
                )
            );

            // The player may FIGHT or use a bag ITEM this turn; the enemy only ever attacks.
            IBattleAction playerAction = await BuildPlayerActionAsync();
            PokemonAttack? enemyMove = await SelectMoveAsync(
                EnemyCreature,
                PlayerCreature,
                _enemyInput
            );
            var enemyAction = new AttackAction(
                EnemyCreature,
                PlayerCreature,
                enemyMove,
                _typeChart,
                _rules,
                _emitter,
                _movePool,
                _rng,
                _escapable
            );

            // Turn resolution: Priority → effective Speed (Paralysis quarters) → random tie-breaker.
            // Draw the tie-break once (a stable sort key) rather than calling RNG inside the
            // comparator, where it would be evaluated an unspecified number of times.
            int tieBreak = _rng.Next(2);
            var turnQueue = new List<IBattleAction> { playerAction, enemyAction };

            turnQueue = turnQueue
                .OrderByDescending(a => a.Priority)
                .ThenByDescending(a => StatusResolver.EffectiveSpeed(a.Source, _rules))
                .ThenBy(a => a == playerAction ? tieBreak : 1 - tieBreak)
                .ToList();

            foreach (var action in turnQueue)
            {
                if (!action.Source.IsAlive())
                    continue;
                // The dead-target and status (sleep/para/confusion) gates are attack-specific. An
                // ItemAction has no foe target and isn't blocked by status — using an item is the turn —
                // so it executes as long as its user is alive.
                if (action is AttackAction attack)
                {
                    if (!attack.Target.IsAlive())
                        continue;
                    if (!StatusResolver.CanAct(action.Source, _rules, _emitter, _rng))
                        continue;
                }
                await action.ExecuteAsync();
            }

            // End-of-turn: binding, Burn, Poison
            StatusResolver.ApplyEndOfTurnDamage(PlayerCreature, _rules, _emitter);
            StatusResolver.ApplyEndOfTurnDamage(EnemyCreature, _rules, _emitter);

            // End-of-turn: Leech Seed drain (must see both creatures, so handled here not in StatusResolver)
            ApplyLeechSeedDrain(PlayerCreature, EnemyCreature);
            ApplyLeechSeedDrain(EnemyCreature, PlayerCreature);

            // Snapshot the flee BEFORE the faint branches: a forced switch-in `continue`s past the flee gate
            // below, so without this a foe already scared off (Roar/Whirlwind) would get a free turn against the
            // incoming creature before the gate is finally read. Read here, while it still describes this turn.
            bool fledThisTurn = PlayerCreature.Battle.HasFled || EnemyCreature.Battle.HasFled;

            if (!EnemyCreature.IsAlive())
            {
                _emitter?.Emit(new CreatureFainted(EnemyCreature.Name));
                // Gen-1 base award (pure, from the seam), then the run's roguelite XP curve — a soft
                // level-aware multiplier keyed on the winner's current level (RunRules, kept out of the seam).
                // The emitted and applied amounts are the same scaled value, so the client's ExperienceGained
                // matches the bar fill.
                int baseXp = _rules.CalculateXpAwarded(
                    EnemyCreature.SpeciesBaseExperience,
                    EnemyCreature.Level,
                    _trainerBattle
                );
                double xpMult = _runRules.XpMultiplierForLevel(PlayerCreature.Level);
                int xp =
                    xpMult == 1.0
                        ? baseXp
                        : Math.Max(
                            0,
                            (int)Math.Round(baseXp * xpMult, MidpointRounding.AwayFromZero)
                        );
                PlayerCreature.AddExperience(xp);
                _emitter?.Emit(new ExperienceGained(PlayerCreature.Name, xp));
                // Gen 1 Stat Exp: the win adds the defeated foe's base stats to the player's accumulated Stat
                // Exp (capped per stat by the calculator). It's silent (no event) and only realizes into
                // actual stats on the next CalculateStats — so award it BEFORE the level-up loop below, so a
                // level gained this battle already reflects the new training.
                // KNOWN DEFECT (TODO.md → "Switched-in creature is the active creature"): only the FINISHER is
                // awarded here. Gen 1 divides Exp and Stat Exp among every creature that was SENT OUT and has not
                // fainted, so this is right only by coincidence — a forced switch leaves the survivor as the sole
                // eligible participant anyway. It will diverge as soon as voluntary switching lands with both
                // creatures alive. Needs a per-battle participant set; do NOT read the old "only the lead earns
                // XP / no Exp Share" note as intent — that pin was wrong and the user corrected it (2026-07-15).
                PlayerCreature.GainStatExp(EnemyCreature);
                // Move learning below mutates the PERMANENT MoveSet. If the player Transformed/Mimicked this
                // battle, MoveSet currently holds the copied moveset and the end-of-battle restore would
                // discard any learn — so revert the player's copied identity first. Learning (and the
                // level-up's stat recompute) then act on the real moveset/stats. The restore is idempotent,
                // so the unconditional one after the loop stays correct for the player-fainted/other paths.
                PlayerCreature.RestoreMimickedMove();
                PlayerCreature.RestoreOriginalIdentity();
                // Drive level-ups one at a time so each event carries that level's resulting stats, the
                // per-stat gains, and bar parameters (also the seam the deferred move-learning will use).
                await RunLevelUpLoopAsync(PlayerCreature, onBench: false);

                // Innate party Exp-Share (roguelite Exp-All, RunRules.BenchXpShare): after the active creature is
                // paid in full above, every LIVING bench member earns a fraction of that same award + the full
                // Stat-Exp, so a drafted roster keeps pace and stays swappable. Fainted members are excluded (a
                // fainted participant earns nothing, per Gen 1). Deliberately a roguelite deviation from Gen 1's
                // participant split — kept out of the seam; scales the seam's result only. Never fires for a direct
                // single-creature Battle (no party threaded) or when the share is 0. Each bench level-up surfaces
                // an attributed LeveledUp + move-learn prompt (same events/name as the active), so the player sees
                // which creature levelled; bench XP itself is silent (no per-member log line) until it does.
                await ShareExperienceWithBenchAsync(xp);
                break;
            }
            if (!PlayerCreature.IsAlive())
            {
                _emitter?.Emit(new CreatureFainted(PlayerCreature.Name));
                // Forced switch-on-faint (Phase 4 Stage 3): if the party still has a live bench member, send one
                // in against the same enemy and keep fighting; the run ends only when the whole party is down.
                // A single-creature battle (no party wired, or no live member left) falls through to the break.
                // Not when a side fled this turn, though: there's no longer a foe on the field to send anyone in
                // against, so the flee gate below owns the ending (the switch would otherwise skip past it).
                if (!fledThisTurn && await TrySwitchInAsync())
                    continue;
                break;
            }

            // Roar / Whirlwind: a side was scared off (no faint). Ends the wild battle — the run loop reads
            // EndedInFlee and advances the encounter without a win/loss. A faint above takes precedence (a KO
            // is a real result); checked here so the fled creature's last action still resolved this turn.
            if (PlayerCreature.Battle.HasFled || EnemyCreature.Battle.HasFled)
            {
                var fled = PlayerCreature.Battle.HasFled ? PlayerCreature : EnemyCreature;
                _emitter?.Emit(new CreatureFled(fled.Name, fled == PlayerCreature));
                EndedInFlee = true;
                break;
            }

            _emitter?.Emit(new TurnEnded());
        }

        // Mimic and Transform/Conversion all revert when the battle ends (Gen 1 also reverts on
        // switch-out) — undo the transient move-swap and any copied identity so neither leaks into the
        // permanent half (MoveSet, types, stats) of a reused Creature.
        PlayerCreature.RestoreMimickedMove();
        EnemyCreature.RestoreMimickedMove();
        PlayerCreature.RestoreOriginalIdentity();
        EnemyCreature.RestoreOriginalIdentity();

        // A flee already announced the end via CreatureFled; the normal win/loss BattleEnded (which the client
        // turns into a "challenger approaches" intermission / game-over) would mis-signal it, so skip it.
        if (!EndedInFlee)
        {
            string winner = PlayerCreature.IsAlive() ? PlayerCreature.Name : EnemyCreature.Name;
            _emitter?.Emit(new BattleEnded(winner));
        }
    }

    /// <summary>
    /// Innate party Exp-Share (roguelite Exp-All): pays each <em>living bench</em> member a fraction
    /// (<see cref="RunRules.BenchXpShare"/>) of the active creature's XP award (<paramref name="activeAward"/>)
    /// plus the full Stat-Exp, then runs its level-up + move-learn loop. The active creature was already paid in
    /// full at the award site, so it is skipped here; a fainted member earns nothing (Gen 1). Each level emits an
    /// attributed <see cref="LeveledUp"/> (carrying the member's name) so the player sees which creature levelled;
    /// bench XP is otherwise silent. No-op without a party or with a zero share — so a direct single-creature
    /// <see cref="Battle"/> is unaffected.
    /// </summary>
    private async Task ShareExperienceWithBenchAsync(int activeAward)
    {
        if (_playerParty is null || _runRules.BenchXpShare <= 0)
            return;

        int share = (int)Math.Floor(activeAward * _runRules.BenchXpShare);
        bool anyLevelled = false;
        foreach (var member in _playerParty.Members)
        {
            if (ReferenceEquals(member, PlayerCreature) || !member.IsAlive())
                continue;

            if (share > 0)
                member.AddExperience(share);
            // Stat-Exp is a coarse, capped accumulator — granted in full to each living member, not fractionalised
            // (and unconditionally: a member still trains off a win even when the fractional XP floors to 0).
            member.GainStatExp(EnemyCreature);

            // Same level-up loop as the active creature (flagged OnBench so the client attributes it without
            // moving the active nameplate), so the surfacing is identical.
            anyLevelled |= await RunLevelUpLoopAsync(member, onBench: true);
        }

        // The bench members' level/stat changes are otherwise invisible: the party strip is fed only by
        // PartyUpdated snapshots (+ the connect-time /party hydrate), so without this its levels/HP would read
        // stale until some later party-carrying event. Push a fresh snapshot when any bench member levelled —
        // the same projection TrySwitchInAsync emits — so the roster panel matches the level-up it just showed.
        if (anyLevelled)
            _emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(_playerParty)));
    }

    /// <summary>
    /// Drives one creature's level-ups one at a time after an XP award: each crossed threshold emits a
    /// <see cref="LeveledUp"/> carrying that level's resulting stats + per-stat gains (and <paramref name="onBench"/>
    /// so the client attributes a benched member's level-up without disturbing the active nameplate), then learns
    /// that level's moves before stepping on — so a multi-level award prompts in canonical Gen 1 order (one move,
    /// one level, at a time). Returns whether at least one level was gained.
    /// </summary>
    private async Task<bool> RunLevelUpLoopAsync(Creature creature, bool onBench)
    {
        bool levelled = false;
        while (true)
        {
            var before = creature.StatSnapshot();
            if (!creature.TryLevelUp())
                break;
            levelled = true;
            var after = creature.StatSnapshot();
            _emitter?.Emit(
                new LeveledUp(
                    creature.Name,
                    creature.Level,
                    creature.XpThisLevel,
                    creature.XpToNextLevel,
                    after,
                    after.Minus(before),
                    OnBench: onBench
                )
            );
            await MoveLearning.LearnMovesForLevelAsync(
                creature,
                creature.Level,
                _emitter,
                _playerInput
            );
        }
        return levelled;
    }

    /// <summary>
    /// Resolves which move a combatant uses this turn. Lock-in mechanics bypass <see cref="IBattleInput"/>:
    /// a two-turn move on its release turn, a rampage (Thrash) while locked in, Bide while committed,
    /// and Rage once used all auto-repeat. Otherwise the input chooses — unless no move is selectable
    /// (out of PP, or the only
    /// option is Disabled), in which case <c>null</c> tells <see cref="AttackAction"/> to Struggle.
    /// The lock branches are checked before <see cref="Creature.CanSelectAnyMove"/>, so a Rage move that
    /// gets Disabled is still force-used (Gen 1's Rage/Disable interaction is nuanced — a documented
    /// simplification, not enforced).
    /// </summary>
    private async Task<PokemonAttack?> SelectMoveAsync(
        Creature attacker,
        Creature defender,
        IBattleInput input
    )
    {
        foreach (var mechanic in LockInMechanics.All)
        {
            if (mechanic.ForcedMove(attacker) is { } forced)
                return forced;
        }
        if (!attacker.CanSelectAnyMove)
            return null;

        return await input.ChooseMoveAsync(
            new TurnContext
            {
                Attacker = attacker,
                Defender = defender,
                TypeChart = _typeChart,
                Rules = _rules,
                TurnNumber = _turnNumber,
                DisabledMove = attacker.Battle.DisabledMove,
            }
        );
    }

    /// <summary>
    /// Builds the player's action for the turn: a bag <see cref="ItemAction"/> if the player chose ITEM
    /// (and a bag is wired), otherwise an <see cref="AttackAction"/>. Lock-in/Struggle take precedence —
    /// the same pre-checks as <see cref="SelectMoveAsync"/> — and on those turns the bag isn't offered (a
    /// locked-in creature can't open the menu), so the action is always a forced/Struggle attack.
    /// </summary>
    private async Task<IBattleAction> BuildPlayerActionAsync()
    {
        var attacker = PlayerCreature;

        foreach (var mechanic in LockInMechanics.All)
        {
            if (mechanic.ForcedMove(attacker) is { } forced)
                return NewPlayerAttack(forced);
        }
        if (!attacker.CanSelectAnyMove)
            return NewPlayerAttack(null); // Struggle

        var context = new TurnContext
        {
            Attacker = attacker,
            Defender = EnemyCreature,
            TypeChart = _typeChart,
            Rules = _rules,
            TurnNumber = _turnNumber,
            DisabledMove = attacker.Battle.DisabledMove,
        };

        var choice = await _playerInput.ChooseTurnActionAsync(context);
        if (choice is ItemTurnChoice item && _playerBag is not null)
            return new ItemAction(attacker, item.Item, item.TargetMoveSlot, _playerBag, _emitter);

        // FIGHT: the input's already-validated move. (A MoveTurnChoice carries it; the only other case —
        // an item choice with no bag wired — falls back to the first selectable move, never Struggle,
        // since CanSelectAnyMove was true above.)
        PokemonAttack? move = choice is MoveTurnChoice mv
            ? mv.Move
            : attacker.MoveSet.FirstOrDefault(m =>
                m.PowerPointsCurrent > 0 && m != attacker.Battle.DisabledMove
            );
        return NewPlayerAttack(move);
    }

    private AttackAction NewPlayerAttack(PokemonAttack? move) =>
        new(
            PlayerCreature,
            EnemyCreature,
            move,
            _typeChart,
            _rules,
            _emitter,
            _movePool,
            _rng,
            _escapable
        );

    /// <summary>
    /// The forced faint-switch (Phase 4 Stage 3). Called after the active player creature has fainted: if the
    /// party is wired and still holds a live bench member, ask the player which one to send in against the same
    /// enemy, bring it in, and return true so the turn loop continues. Returns false — the battle ends as a loss —
    /// when there's no party or every member is down (a single-creature battle always returns false, its legacy
    /// behaviour). The replacement does <em>not</em> act the turn it enters (this turn already resolved) and the
    /// enemy is untouched — canonical Gen 1.
    /// </summary>
    private async Task<bool> TrySwitchInAsync()
    {
        if (_playerParty is null || FirstLiveMemberIndex() < 0)
            return false;

        // The outgoing (fainted) creature leaves the field. Undo any Mimic/Transform it copied this battle before
        // it benches, so a transformed-then-fainted creature can't leak its copied moveset/stats into its
        // permanent half (the end-of-battle restore only reaches the creature that's active then).
        PlayerCreature.RestoreMimickedMove();
        PlayerCreature.RestoreOriginalIdentity();

        _emitter?.Emit(
            new SwitchInOffered(PartyProjection.Snapshot(_playerParty), PlayerCreature.Name)
        );
        int index = await _playerInput.ChooseSwitchInAsync(new SwitchInContext(_playerParty));
        // Never send in a fainted / out-of-range creature: correct a stale or malformed pick to the first live
        // member (the interface default already picks a live one, but the web hub can forward an arbitrary int).
        if (index < 0 || index >= _playerParty.Count || !_playerParty.Members[index].IsAlive())
            index = FirstLiveMemberIndex();

        _playerParty.SetLead(index);
        PlayerCreature = _playerParty.Lead;
        PlayerCreature.ResetBattleState();
        // The incoming creature enters on its OWN carried major status (Gen 1 keeps sleep/poison/burn/etc. on a
        // benched creature) — the same entry-status application as a fresh battle's lead. Volatiles were just
        // cleared by the reset; the previous creature's status can't leak (each member carries its own).
        ApplyEntryStatus(PlayerCreature.CarriedStatus);

        _emitter?.Emit(
            new CreatureSwitchedIn(
                PlayerCreature.Name,
                PlayerCreature.SpeciesId,
                PlayerCreature.Level,
                PlayerCreature.Attributes.HP,
                PlayerCreature.Attributes.MaxHP,
                PlayerCreature.Battle.Status
            )
        );
        _emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(_playerParty)));
        return true;
    }

    /// <summary>
    /// Applies a creature's carried out-of-battle major status as it takes the field, on top of the freshly
    /// reset <see cref="BattleState"/>. The single rule for both entry points — the battle's opening lead
    /// (from the ctor's carried status) and a forced faint-switch send-in (from the incoming member's own
    /// <see cref="Creature.CarriedStatus"/>) — so the two can never drift apart. Only the major status and its
    /// sleep counter cross; volatiles are deliberately left cleared (Gen 1).
    /// </summary>
    private void ApplyEntryStatus(CarriedStatus? carried)
    {
        if (carried is { Status: not StatusCondition.None } entry)
        {
            PlayerCreature.Battle.Status = entry.Status;
            PlayerCreature.Battle.SleepTurns = entry.SleepTurns;
        }
    }

    // The index of the first alive party member, or -1 if the whole party is down. The active (fainted) creature
    // reads dead here too, so this only ever returns a benched live member (or -1 → the run is over).
    private int FirstLiveMemberIndex()
    {
        if (_playerParty is null)
            return -1;
        for (int i = 0; i < _playerParty.Count; i++)
            if (_playerParty.Members[i].IsAlive())
                return i;
        return -1;
    }

    private void ApplyLeechSeedDrain(Creature drained, Creature healed)
    {
        if (!drained.Battle.HasLeechSeed || !drained.IsAlive())
            return;

        int damage = Math.Max(1, drained.Attributes.MaxHP / _rules.PoisonDamageDenominator);
        drained.Attributes.ReceiveDamage(damage);
        _emitter?.Emit(new LeechSeedDamage(drained.Name, damage, drained.Attributes.HP));

        if (healed.IsAlive())
        {
            healed.Attributes.ReceiveHealing(damage);
            _emitter?.Emit(new LeechSeedHealed(healed.Name, damage, healed.Attributes.HP));
        }
    }
}
