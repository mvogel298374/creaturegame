using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public class Battle
{
    private Creature PlayerCreature { get; }
    private Creature EnemyCreature  { get; }
    private readonly ITypeChart              _typeChart;
    private readonly IBattleRules            _rules;
    private readonly IBattleInput            _playerInput;
    private readonly IBattleInput            _enemyInput;
    private readonly IBattleEventEmitter?    _emitter;
    private readonly IReadOnlyList<Attack>   _movePool;
    private readonly IRandomSource           _rng;
    private int _turnNumber;

    public Battle(Creature player, Creature enemy, ITypeChart typeChart,
                  IBattleInput playerInput, IBattleInput enemyInput,
                  IReadOnlyList<Attack>? movePool = null,
                  IBattleRules? rules = null, IBattleEventEmitter? emitter = null,
                  IRandomSource? rng = null)
    {
        PlayerCreature = player;
        EnemyCreature  = enemy;
        _typeChart     = typeChart;
        _rules         = rules ?? Gen1BattleRules.Instance;
        _playerInput   = playerInput;
        _enemyInput    = enemyInput;
        _movePool      = movePool ?? Array.Empty<Attack>();
        _emitter       = emitter;
        _rng           = rng ?? SystemRandomSource.Instance;
    }

    public async Task StartFightAsync()
    {
        PlayerCreature.ResetBattleState();
        EnemyCreature.ResetBattleState();

        _emitter?.Emit(new BattleStarted(PlayerCreature.Name, EnemyCreature.Name, EnemyCreature.SpeciesId, EnemyCreature.Level));

        while (PlayerCreature.IsAlive() && EnemyCreature.IsAlive())
        {
            _turnNumber++;

            _emitter?.Emit(new TurnStarted(
                _turnNumber,
                PlayerCreature.Name, PlayerCreature.Attributes.HP, PlayerCreature.Attributes.MaxHP, PlayerCreature.Status,
                EnemyCreature.Name,  EnemyCreature.Attributes.HP,  EnemyCreature.Attributes.MaxHP,  EnemyCreature.Status,
                PlayerCreature.MoveSet.Select(m => new MoveInfo(m.Base.Name ?? "", m.Base.DamageType, m.PowerPointsCurrent, m.Base.PowerPointsMax, m == PlayerCreature.DisabledMove)).ToList()
            ));

            // Move selection — two-turn moves skip IBattleInput on the release turn and rampage
            // moves (Thrash) skip it while locked in; null signals AttackAction to Struggle when
            // out of PP.
            PokemonAttack? playerMove = PlayerCreature.IsTwoTurnCharging
                ? PlayerCreature.ChargingMove
                : PlayerCreature.RampageTurnsRemaining > 0
                  ? PlayerCreature.RampageMove
                  : (!PlayerCreature.CanSelectAnyMove
                    ? null
                    : await _playerInput.ChooseMoveAsync(new TurnContext
                      {
                          Attacker     = PlayerCreature,
                          Defender     = EnemyCreature,
                          TypeChart    = _typeChart,
                          Rules        = _rules,
                          TurnNumber   = _turnNumber,
                          DisabledMove = PlayerCreature.DisabledMove
                      }));

            PokemonAttack? enemyMove = EnemyCreature.IsTwoTurnCharging
                ? EnemyCreature.ChargingMove
                : EnemyCreature.RampageTurnsRemaining > 0
                  ? EnemyCreature.RampageMove
                  : (!EnemyCreature.CanSelectAnyMove
                    ? null
                    : await _enemyInput.ChooseMoveAsync(new TurnContext
                      {
                          Attacker     = EnemyCreature,
                          Defender     = PlayerCreature,
                          TypeChart    = _typeChart,
                          Rules        = _rules,
                          TurnNumber   = _turnNumber,
                          DisabledMove = EnemyCreature.DisabledMove
                      }));

            var playerAction = new AttackAction(PlayerCreature, EnemyCreature, playerMove, _typeChart, _rules, _emitter, _movePool, _rng);
            var enemyAction  = new AttackAction(EnemyCreature, PlayerCreature, enemyMove,  _typeChart, _rules, _emitter, _movePool, _rng);

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
                if (!action.Source.IsAlive()) continue;
                if ((action as AttackAction)?.Target.IsAlive() != true) continue;
                if (!StatusResolver.CanAct(action.Source, _rules, _emitter, _rng)) continue;
                await action.ExecuteAsync();
            }

            // End-of-turn: binding, Burn, Poison
            StatusResolver.ApplyEndOfTurnDamage(PlayerCreature, _rules, _emitter);
            StatusResolver.ApplyEndOfTurnDamage(EnemyCreature,  _rules, _emitter);

            // End-of-turn: Leech Seed drain (must see both creatures, so handled here not in StatusResolver)
            ApplyLeechSeedDrain(PlayerCreature, EnemyCreature);
            ApplyLeechSeedDrain(EnemyCreature,  PlayerCreature);

            if (!EnemyCreature.IsAlive())
            {
                _emitter?.Emit(new CreatureFainted(EnemyCreature.Name));
                int xp = _rules.CalculateXpAwarded(EnemyCreature.SpeciesBaseExperience, EnemyCreature.Level);
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

        string winner = PlayerCreature.IsAlive() ? PlayerCreature.Name : EnemyCreature.Name;
        _emitter?.Emit(new BattleEnded(winner));
    }

    private void ApplyLeechSeedDrain(Creature drained, Creature healed)
    {
        if (!drained.HasLeechSeed || !drained.IsAlive()) return;

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
