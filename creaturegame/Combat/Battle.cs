using creaturegame.Attacks;
using creaturegame.Creatures;

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
        CarriedStatus? playerEntryStatus = null
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
                            m == PlayerCreature.Battle.DisabledMove
                        ))
                        .ToList()
                )
            );

            PokemonAttack? playerMove = await SelectMoveAsync(
                PlayerCreature,
                EnemyCreature,
                _playerInput
            );
            PokemonAttack? enemyMove = await SelectMoveAsync(
                EnemyCreature,
                PlayerCreature,
                _enemyInput
            );

            var playerAction = new AttackAction(
                PlayerCreature,
                EnemyCreature,
                playerMove,
                _typeChart,
                _rules,
                _emitter,
                _movePool,
                _rng
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
                if ((action as AttackAction)?.Target.IsAlive() != true)
                    continue;
                if (!StatusResolver.CanAct(action.Source, _rules, _emitter, _rng))
                    continue;
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
                    await LearnMovesForLevelAsync(PlayerCreature, PlayerCreature.Level);
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
    /// Teaches the creature every move it learns at <paramref name="level"/>. A free slot auto-learns; a full
    /// moveset emits <see cref="MoveReplacementRequired"/> and blocks on the player's input — a chosen slot
    /// (0–3) is replaced, <c>null</c> declines (canonical Gen 1 "don't learn"). Driven once per level so a
    /// multi-level award prompts in order. Only the player ever learns, so this always uses the player input.
    /// </summary>
    private async Task LearnMovesForLevelAsync(Creature learner, int level)
    {
        foreach (var move in learner.MovesLearnedAtLevel(level).ToList())
        {
            if (learner.AddAttack(move))
            {
                _emitter?.Emit(new MoveLearned(learner.Name, move.Name ?? ""));
                continue;
            }

            // Four slots full — ask the player which move to forget (or to decline).
            _emitter?.Emit(
                new MoveReplacementRequired(
                    learner.Name,
                    move.Name ?? "",
                    learner.MoveSet.Select(m => m.Base.Name ?? "").ToList()
                )
            );
            int? slot = await _playerInput.ChooseMoveToForgetAsync(
                new MoveReplacementContext(learner, move)
            );
            if (slot is int s && s >= 0 && s < learner.MoveSet.Count)
            {
                string forgotten = learner.MoveSet[s].Base.Name ?? "";
                learner.ReplaceMove(s, move);
                _emitter?.Emit(new MoveForgotten(learner.Name, forgotten));
                _emitter?.Emit(new MoveLearned(learner.Name, move.Name ?? ""));
            }
            else
            {
                // null / out of range → declined: the moveset is unchanged.
                _emitter?.Emit(new MoveLearnDeclined(learner.Name, move.Name ?? ""));
            }
        }
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
