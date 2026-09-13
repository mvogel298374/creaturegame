using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

public class Battle
{
    // Reassignable — a forced or voluntary switch brings in a bench member mid-battle (STATE_MODEL.md §2).
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

    // Null keeps the legacy single-creature behaviour (a faint ends the battle) for every direct Battle caller.
    private readonly Party? _playerParty;

    // The Gen 1 participant set a win's XP is divided among (STATE_MODEL.md §2). Reference identity is safe:
    // Creature overrides neither Equals nor GetHashCode.
    private readonly HashSet<Creature> _participants = new();

    private int _turnNumber;

    /// <summary>True on a Roar/Whirlwind flee (wild only) rather than a faint — the run loop advances the
    /// encounter without a win/loss or XP.</summary>
    public bool EndedInFlee { get; private set; }

    /// <summary>True on the player's win, independent of whether the finisher itself survived it (a mutual
    /// KO) — the enemy-faint check runs first. STATE_MODEL.md §2 / TODO_ARCHIVE.md → "Mutual KO ends the run
    /// even with a live bench".</summary>
    public bool PlayerWon { get; private set; }

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
        // Trainer-owned foe (Elite/Boss) → the Gen-1 seam's ×1.5 XP bonus. Default false = wild.
        bool trainerBattle = false,
        // Roguelite dial on top of the Gen-1 seam, NOT a seam itself — GENERATION_SEAMS.md. Default is a no-op.
        RunRules? runRules = null,
        // Forced-switch-on-faint when supplied; `player` must be its current Lead. Null = legacy single-creature.
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

        _participants.Add(PlayerCreature);
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
                                : 1.0,
                            // Raw base power for the menu's strength cue — plain move data (fixed-damage/status
                            // moves have BaseDamage 0, so they carry no power and the UI shows no pill).
                            m.Base.BaseDamage
                        ))
                        .ToList(),
                    CanSwitchThisTurn()
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
                // so it executes as long as its user is alive. A SwitchAction likewise has no target and
                // isn't gated by status (Gen 1: only trapping blocks a switch, checked at build time).
                if (action is AttackAction attack)
                {
                    if (!attack.Target.IsAlive())
                        continue;
                    if (!StatusResolver.CanAct(action.Source, _rules, _emitter, _rng))
                        continue;
                }
                await action.ExecuteAsync();

                // A voluntary switch (which always sorts first) reassigned the active creature mid-turn. The
                // enemy's action was built against the creature that just LEFT the field — repoint every
                // still-queued attack at the one that came in, so a slower (or priority) enemy move this turn
                // lands on the switch-in, not the benched creature. Only the enemy's action remains here (the
                // enemy never switches), so this touches exactly it.
                if (action is SwitchAction)
                    foreach (var queued in turnQueue)
                        if (queued is AttackAction toRetarget)
                            toRetarget.Retarget(PlayerCreature);
            }

            // A suppression set but never consumed this turn is stale, not a pending block (next turn's move
            // selection overwrites it before it's read again) — must not leak into next turn's CanAct.
            PlayerCreature.Battle.HazeSuppressedStatus = null;
            EnemyCreature.Battle.HazeSuppressedStatus = null;

            // Disable/binding countdowns always tick, even on a turn a faint ends early below — unlike the
            // residual phase, these are NOT part of IBattleRules.FaintEndsTurnImmediately.
            StatusResolver.TickTurnCounters(PlayerCreature, _emitter);
            StatusResolver.TickTurnCounters(EnemyCreature, _emitter);

            // IBattleRules.FaintEndsTurnImmediately — TODO_ARCHIVE.md → "End-of-turn residual … fired even
            // after a same-turn faint" for the incident this guards against.
            if (
                !_rules.FaintEndsTurnImmediately
                || (PlayerCreature.IsAlive() && EnemyCreature.IsAlive())
            )
            {
                // End-of-turn: Burn, Poison
                StatusResolver.ApplyEndOfTurnDamage(PlayerCreature, _rules, _emitter);
                StatusResolver.ApplyEndOfTurnDamage(EnemyCreature, _rules, _emitter);

                // End-of-turn: Leech Seed drain (must see both creatures, so handled here not in StatusResolver)
                ApplyLeechSeedDrain(PlayerCreature, EnemyCreature);
                ApplyLeechSeedDrain(EnemyCreature, PlayerCreature);
            }

            // Snapshot the flee BEFORE the faint branches: a forced switch-in `continue`s past the flee gate
            // below, so without this a foe already scared off (Roar/Whirlwind) would get a free turn against the
            // incoming creature before the gate is finally read. Read here, while it still describes this turn.
            bool fledThisTurn = PlayerCreature.Battle.HasFled || EnemyCreature.Battle.HasFled;

            if (!EnemyCreature.IsAlive())
            {
                PlayerWon = true;
                _emitter?.Emit(new CreatureFainted(EnemyCreature.Name));
                // A MUTUAL KO drops the player's creature on the same turn, and this branch breaks out before the
                // losing-faint branch below (the only other CreatureFainted emitter) — so without this the client
                // would never play the player-side faint animation/cry: its creature would just sit at an empty HP
                // bar through the victory. Emitted after the enemy's, matching the check order that makes the
                // trade a win. Cannot double-emit: the branch below is unreachable once we break here.
                if (!PlayerCreature.IsAlive())
                    _emitter?.Emit(new CreatureFainted(PlayerCreature.Name));
                // Gen-1 base award, then the roguelite XP curve on top (GENERATION_SEAMS.md).
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
                // Gen 1 participation split (STATE_MODEL.md §2) — Battle only decides WHO participated; the
                // division itself is gen-variable and lives on the seam.
                var participants = LiveParticipants();
                int participantShare = _rules.SplitXpAmongParticipants(xp, participants.Count);

                bool anyLevelled = false; // gates the one PartyUpdated snapshot below

                if (PlayerCreature.IsAlive())
                {
                    PlayerCreature.AddExperience(participantShare);
                    _emitter?.Emit(new ExperienceGained(PlayerCreature.Name, participantShare));
                    // Award Stat-Exp before the level-up loop so a level gained this battle already reflects it.
                    PlayerCreature.GainStatExp(EnemyCreature);
                    // Revert a Transform/Mimic copy before learning mutates the PERMANENT MoveSet.
                    PlayerCreature.RestoreMimickedMove();
                    PlayerCreature.RestoreOriginalIdentity();
                    anyLevelled = await RunLevelUpLoopAsync(PlayerCreature, onBench: false);
                }

                anyLevelled |= await PayOtherParticipantsAsync(participants, participantShare);
                anyLevelled |= await ShareExperienceWithBenchAsync(xp); // innate bench share — STATE_MODEL.md §2

                // Covers all three award loops above, including the on-field creature's own level-up: its
                // nameplate is driven by LeveledUp directly, but its party-strip row is not.
                if (anyLevelled && _playerParty is not null)
                    _emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(_playerParty)));
                break;
            }
            if (!PlayerCreature.IsAlive())
            {
                _emitter?.Emit(new CreatureFainted(PlayerCreature.Name));
                // Not when a side fled this turn — no foe left to send anyone in against, so the flee gate
                // below owns the ending instead.
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
            // Keyed on PlayerWon, NOT on who is still standing: a mutual KO leaves the finisher fainted but is
            // still the player's win, and naming the (also fainted) enemy would tell the client it lost — which
            // is how it decides between the intermission beat and waiting for a game-over.
            string winner = PlayerWon ? PlayerCreature.Name : EnemyCreature.Name;
            _emitter?.Emit(new BattleEnded(winner));
        }
    }

    /// <summary>The Gen 1 participant set a win's XP is divided among (STATE_MODEL.md §2), in roster order for
    /// deterministic award events. Can legitimately be empty (a single-creature mutual KO).</summary>
    private List<Creature> LiveParticipants()
    {
        var live = new List<Creature>();
        if (_playerParty is not null)
        {
            foreach (var member in _playerParty.Members)
            {
                if (_participants.Contains(member) && member.IsAlive())
                    live.Add(member);
            }
        }

        // Covers the party-less battle; IsAlive() keeps a mutual KO's fainted finisher excluded.
        if (PlayerCreature.IsAlive() && !live.Contains(PlayerCreature))
            live.Insert(0, PlayerCreature);

        return live;
    }

    /// <summary>Pays every live participant other than the active creature its equal <paramref name="share"/>
    /// (STATE_MODEL.md §2) — skips the active creature, already paid at the award site (the "paid once"
    /// invariant). Returns whether any of them levelled.</summary>
    private async Task<bool> PayOtherParticipantsAsync(
        IReadOnlyList<Creature> participants,
        int share
    )
    {
        bool anyLevelled = false;
        foreach (var member in participants)
        {
            if (ReferenceEquals(member, PlayerCreature))
                continue;

            if (share > 0)
                member.AddExperience(share);
            _emitter?.Emit(new ExperienceGained(member.Name, share, OnBench: true));
            member.GainStatExp(EnemyCreature);

            // Its Mimic/Transform identity was already restored by RestoreOutgoing() as it left the field.
            anyLevelled |= await RunLevelUpLoopAsync(member, onBench: true);
        }

        return anyLevelled;
    }

    /// <summary>Innate party Exp-Share: pays each living non-participant a fraction of the FULL award
    /// (<see cref="RunRules.BenchXpShare"/>) — STATE_MODEL.md §2, incl. the known bench-vs-participant balance
    /// inversion. No-op without a party or with a zero share. Returns whether any bench member levelled.</summary>
    private async Task<bool> ShareExperienceWithBenchAsync(int fullAward)
    {
        if (_playerParty is null || _runRules.BenchXpShare <= 0)
            return false;

        int share = (int)Math.Floor(fullAward * _runRules.BenchXpShare);
        bool anyLevelled = false;
        foreach (var member in _playerParty.Members)
        {
            if (_participants.Contains(member) || !member.IsAlive())
                continue;

            // Skipped when share floors to 0 so Stat-Exp-only training stays silent (no "gained 0 EXP" line).
            if (share > 0)
            {
                member.AddExperience(share);
                _emitter?.Emit(new ExperienceGained(member.Name, share, OnBench: true));
            }
            member.GainStatExp(EnemyCreature);
            anyLevelled |= await RunLevelUpLoopAsync(member, onBench: true);
        }

        return anyLevelled;
    }

    /// <summary>Drives one creature's level-ups one at a time, learning that level's moves before stepping on —
    /// canonical Gen 1 order for a multi-level award. Returns whether at least one level was gained.</summary>
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

    /// <summary>Resolves which move a combatant uses. Lock-in mechanics (two-turn release, rampage, Bide,
    /// Rage) bypass <see cref="IBattleInput"/> and auto-repeat, checked before
    /// <see cref="Creature.CanSelectAnyMove"/> so a Disabled Rage move is still force-used (a known,
    /// unenforced simplification of Gen 1's Rage/Disable nuance). Otherwise the input chooses, or
    /// <c>null</c> for Struggle when nothing is selectable.</summary>
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

    /// <summary>Builds the player's action: ITEM/SWITCH/FIGHT. A true lock-in bypasses the menu entirely. The
    /// whole-turn menu is otherwise offered even out of PP (BAG/SWITCH stay reachable — Gen 1); an illegal
    /// ITEM/SWITCH pick falls through to FIGHT (Struggle if nothing is selectable) rather than stranding the
    /// turn.</summary>
    private async Task<IBattleAction> BuildPlayerActionAsync()
    {
        var attacker = PlayerCreature;

        // A TRUE lock-in owns the whole turn — no menu at all. Struggle is NOT a lock-in: out of PP the menu
        // still shows below (only a FIGHT with nothing selectable becomes Struggle), so it is not checked here.
        foreach (var mechanic in LockInMechanics.All)
        {
            if (mechanic.ForcedMove(attacker) is { } forced)
                return NewPlayerAttack(forced);
        }

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
        switch (choice)
        {
            case ItemTurnChoice item when _playerBag is not null:
                return new ItemAction(
                    attacker,
                    item.Item,
                    item.TargetMoveSlot,
                    _playerBag,
                    _emitter,
                    _playerParty,
                    item.TargetPartySlot
                );

            case SwitchTurnChoice sw when CanSwitchTo(sw.PartyIndex):
                return new SwitchAction(this, attacker, sw.PartyIndex);

            case MoveTurnChoice mv when attacker.CanSelectAnyMove:
                // FIGHT: the input's already-validated move.
                return NewPlayerAttack(mv.Move);

            default:
                // FIGHT fallback for every remaining case — an explicit Struggle, an ITEM with no bag wired, an
                // illegal SWITCH, or a MoveTurnChoice from an out-of-PP creature: pick the first selectable move,
                // or null (Struggle) when nothing is selectable.
                PokemonAttack? move = attacker.CanSelectAnyMove
                    ? attacker.MoveSet.FirstOrDefault(m =>
                        m.PowerPointsCurrent > 0 && m != attacker.Battle.DisabledMove
                    )
                    : null;
                return NewPlayerAttack(move);
        }
    }

    /// <summary>Legal to switch to <paramref name="index"/> this turn: a party is wired, the active creature
    /// isn't trapped by a partial-trap bind (the one thing that blocks switching in Gen 1 — status does not),
    /// and the target is in range, alive, and not already active.</summary>
    private bool CanSwitchTo(int index) =>
        _playerParty is not null
        && PlayerCreature.Battle.BindingTurnsRemaining == 0
        && index >= 0
        && index < _playerParty.Count
        && index != _playerParty.LeadIndex
        && _playerParty.Members[index].IsAlive();

    /// <summary>Projected onto <see cref="TurnStarted"/> so the client can grey the SWITCH button proactively —
    /// independent of PP (Gen 1 allows switching with no usable move).</summary>
    private bool CanSwitchThisTurn()
    {
        if (_playerParty is null)
            return false;
        // Same predicate the menu bypass uses, so the two can't drift apart and advertise a switch the server
        // then refuses.
        if (LockInMechanics.All.Any(m => m.IsLockedIn(PlayerCreature)))
            return false;
        for (int i = 0; i < _playerParty.Count; i++)
            if (CanSwitchTo(i))
                return true;
        return false;
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

    /// <summary>The forced faint-switch: if a live bench member remains, ask the player which to send in and
    /// continue the turn loop; false (battle ends as a loss) when there's none. The replacement does not act
    /// the turn it enters — canonical Gen 1.</summary>
    private async Task<bool> TrySwitchInAsync()
    {
        if (_playerParty is null || FirstLiveMemberIndex() < 0)
            return false;

        // No status captured: it fainted, and a fainted member carries nothing.
        RestoreOutgoing();

        _emitter?.Emit(
            new SwitchInOffered(PartyProjection.Snapshot(_playerParty), PlayerCreature.Name)
        );
        int index = await _playerInput.ChooseSwitchInAsync(new SwitchInContext(_playerParty));
        // Never send in a fainted/out-of-range creature — the rule lives on Party so this and the run loop's
        // post-mutual-KO promotion can't drift apart.
        BringInMember(_playerParty.CorrectSwitchInPick(index));
        return true;
    }

    /// <summary>The voluntary in-battle switch. Unlike the forced path the outgoing creature is still alive, so
    /// its major status is captured onto its own <see cref="Creature.CarriedStatus"/> first (Gen 1 keeps status
    /// through a switch-out). Called from <see cref="SwitchAction"/> once validated by <see cref="CanSwitchTo"/>.
    /// </summary>
    internal void PerformVoluntarySwitch(int index)
    {
        PlayerCreature.CarriedStatus = CarriedStatus.Capture(_rules, PlayerCreature);
        RestoreOutgoing();
        BringInMember(index);
    }

    /// <summary>The outgoing creature leaves the field: undo any Mimic/Transform it copied this battle so a
    /// transformed creature can't leak its copied moveset/stats onto the bench (the end-of-battle restore only
    /// reaches whichever creature is active then). Shared by the forced and voluntary switch-out paths.</summary>
    private void RestoreOutgoing()
    {
        PlayerCreature.RestoreMimickedMove();
        PlayerCreature.RestoreOriginalIdentity();
    }

    /// <summary>Brings the party member at <paramref name="index"/> onto the field — the shared tail of both
    /// switch paths. Resets volatiles, re-applies its OWN carried status (nothing leaks from the creature that
    /// left), and records participation.</summary>
    private void BringInMember(int index)
    {
        _playerParty!.SetLead(index);
        PlayerCreature = _playerParty.Lead;
        PlayerCreature.ResetBattleState();
        ApplyEntryStatus(PlayerCreature.CarriedStatus);
        _participants.Add(PlayerCreature);

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

    private int FirstLiveMemberIndex() => _playerParty?.FirstLiveIndex() ?? -1;

    private void ApplyLeechSeedDrain(Creature drained, Creature healed)
    {
        if (!drained.Battle.HasLeechSeed || !drained.IsAlive())
            return;

        int damage = Math.Max(1, drained.Attributes.MaxHP / _rules.LeechSeedDrainDenominator);
        drained.Attributes.ReceiveDamage(damage);
        _emitter?.Emit(new LeechSeedDamage(drained.Name, damage, drained.Attributes.HP));

        if (healed.IsAlive())
        {
            healed.Attributes.ReceiveHealing(damage);
            _emitter?.Emit(new LeechSeedHealed(healed.Name, damage, healed.Attributes.HP));
        }
    }
}
