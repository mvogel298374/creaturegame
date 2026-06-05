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
        IRandomSource? rng = null
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
    }

    public async Task StartFightAsync()
    {
        PlayerCreature.ResetBattleState();
        EnemyCreature.ResetBattleState();

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
                    PlayerCreature.Status,
                    EnemyCreature.Name,
                    EnemyCreature.Attributes.HP,
                    EnemyCreature.Attributes.MaxHP,
                    EnemyCreature.Status,
                    PlayerCreature
                        .MoveSet.Select(m => new MoveInfo(
                            m.Base.Name ?? "",
                            m.Base.DamageType,
                            m.PowerPointsCurrent,
                            m.Base.PowerPointsMax,
                            m == PlayerCreature.DisabledMove
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
                int priorLevel = PlayerCreature.Level;
                PlayerCreature.GainExperience(xp);
                while (priorLevel < PlayerCreature.Level)
                {
                    priorLevel++;
                    _emitter?.Emit(new LeveledUp(PlayerCreature.Name, priorLevel));
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

        // Mimic reverts when the battle ends (Gen 1 also reverts on switch-out) — undo the transient
        // move-swap so it never leaks into the permanent MoveSet of a reused Creature.
        PlayerCreature.RestoreMimickedMove();
        EnemyCreature.RestoreMimickedMove();

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
        if (attacker.IsTwoTurnCharging)
            return attacker.ChargingMove;
        if (attacker.RampageTurnsRemaining > 0)
            return attacker.RampageMove;
        if (attacker.BideTurnsRemaining > 0)
            return attacker.BideMove;
        if (attacker.IsRaging)
            return attacker.RageMove;
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
                DisabledMove = attacker.DisabledMove,
            }
        );
    }

    private void ApplyLeechSeedDrain(Creature drained, Creature healed)
    {
        if (!drained.HasLeechSeed || !drained.IsAlive())
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
