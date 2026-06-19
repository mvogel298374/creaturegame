using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

public class Battle
{
    private Creature PlayerCreature { get; }
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
    private int _turnNumber;

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
        Bag? playerBag = null
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
    }

    public async Task StartFightAsync()
    {
        PlayerCreature.ResetBattleState();
        EnemyCreature.ResetBattleState();

        // A player carried over from a previous encounter in an endless run keeps its major status — Gen 1
        // persists status out of battle, but the per-battle reset above just cleared it, so re-apply.
        // Volatiles (confusion, stat stages, …) are deliberately NOT carried. Enemies are always freshly
        // built, so they never carry anything.
        if (_playerEntryStatus is { Status: not StatusCondition.None } entry)
        {
            PlayerCreature.Battle.Status = entry.Status;
            PlayerCreature.Battle.SleepTurns = entry.SleepTurns;
        }

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
                _rng
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

            if (!EnemyCreature.IsAlive())
            {
                _emitter?.Emit(new CreatureFainted(EnemyCreature.Name));
                int xp = _rules.CalculateXpAwarded(
                    EnemyCreature.SpeciesBaseExperience,
                    EnemyCreature.Level
                );
                PlayerCreature.AddExperience(xp);
                _emitter?.Emit(new ExperienceGained(PlayerCreature.Name, xp));
                // Gen 1 Stat Exp: the win adds the defeated foe's base stats to the player's accumulated Stat
                // Exp (capped per stat by the calculator). It's silent (no event) and only realizes into
                // actual stats on the next CalculateStats — so award it BEFORE the level-up loop below, so a
                // level gained this battle already reflects the new training. Single-participant scope: one
                // player creature, no switching, so the finisher is the only participant — a multi-mon party
                // would instead call GainStatExp once per participant that was sent out against this foe.
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
                while (true)
                {
                    var before = PlayerCreature.StatSnapshot();
                    if (!PlayerCreature.TryLevelUp())
                        break;
                    var after = PlayerCreature.StatSnapshot();
                    _emitter?.Emit(
                        new LeveledUp(
                            PlayerCreature.Name,
                            PlayerCreature.Level,
                            PlayerCreature.XpThisLevel,
                            PlayerCreature.XpToNextLevel,
                            after,
                            after.Minus(before)
                        )
                    );
                    // Learn this level's moves before stepping to the next level, so a multi-level award
                    // prompts in canonical order (one move, one level, at a time).
                    await MoveLearning.LearnMovesForLevelAsync(
                        PlayerCreature,
                        PlayerCreature.Level,
                        _emitter,
                        _playerInput
                    );
                }
                break;
            }
            if (!PlayerCreature.IsAlive())
            {
                _emitter?.Emit(new CreatureFainted(PlayerCreature.Name));
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

        string winner = PlayerCreature.IsAlive() ? PlayerCreature.Name : EnemyCreature.Name;
        _emitter?.Emit(new BattleEnded(winner));
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
        new(PlayerCreature, EnemyCreature, move, _typeChart, _rules, _emitter, _movePool, _rng);

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
