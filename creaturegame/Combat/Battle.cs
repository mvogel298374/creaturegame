using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public class Battle
{
    private Creature PlayerCreature { get; }
    private Creature EnemyCreature  { get; }
    private readonly ITypeChart   _typeChart;
    private readonly IBattleRules _rules;
    private readonly IBattleInput _playerInput;
    private readonly IBattleInput _enemyInput;
    private int _turnNumber;

    /// <summary>
    /// Creates a battle.
    /// Pass the generation-appropriate <paramref name="typeChart"/> and
    /// <paramref name="rules"/> to control type effectiveness and battle mechanics.
    /// Use <see cref="AutoSelectInput.Instance"/> for sides not yet wired to a real input.
    /// </summary>
    public Battle(Creature player, Creature enemy, ITypeChart typeChart,
                  IBattleInput playerInput, IBattleInput enemyInput,
                  IBattleRules? rules = null)
    {
        PlayerCreature = player;
        EnemyCreature  = enemy;
        _typeChart     = typeChart;
        _rules         = rules ?? Gen1BattleRules.Instance;
        _playerInput   = playerInput;
        _enemyInput    = enemyInput;
    }

    public async Task StartFightAsync()
    {
        Console.WriteLine($"A wild {EnemyCreature.Name} appeared!");
        Console.WriteLine($"Go! {PlayerCreature.Name}!");

        while (PlayerCreature.IsAlive() && EnemyCreature.IsAlive())
        {
            _turnNumber++;

            Console.WriteLine($"\n{PlayerCreature.Name}: {PlayerCreature.Attributes.HP}/{PlayerCreature.Attributes.MaxHP} HP");
            Console.WriteLine($"{EnemyCreature.Name}: {EnemyCreature.Attributes.HP}/{EnemyCreature.Attributes.MaxHP} HP");

            // Move selection — IBattleInput is bypassed when a creature is out of PP;
            // null signals AttackAction to use Struggle (system-enforced, not a player/AI choice).
            PokemonAttack? playerMove = PlayerCreature.IsOutOfPP
                ? null
                : await _playerInput.ChooseMoveAsync(new TurnContext
                  {
                      Attacker  = PlayerCreature,
                      Defender  = EnemyCreature,
                      TypeChart = _typeChart,
                      TurnNumber = _turnNumber
                  });

            PokemonAttack? enemyMove = EnemyCreature.IsOutOfPP
                ? null
                : await _enemyInput.ChooseMoveAsync(new TurnContext
                  {
                      Attacker  = EnemyCreature,
                      Defender  = PlayerCreature,
                      TypeChart = _typeChart,
                      TurnNumber = _turnNumber
                  });

            var playerAction = new AttackAction(PlayerCreature, EnemyCreature, playerMove, _typeChart, _rules);
            var enemyAction  = new AttackAction(EnemyCreature, PlayerCreature, enemyMove,  _typeChart, _rules);

            // Turn resolution: Priority → effective Speed (Paralysis quarters) → random tie-breaker
            var turnQueue = new List<IBattleAction> { playerAction, enemyAction };

            turnQueue = turnQueue
                .OrderByDescending(a => a.Priority)
                .ThenByDescending(a => StatusResolver.EffectiveSpeed(a.Source))
                .ThenBy(_ => Random.Shared.Next())
                .ToList();

            foreach (var action in turnQueue)
            {
                if (!action.Source.IsAlive()) continue;
                if ((action as AttackAction)?.Target.IsAlive() != true) continue;
                if (!StatusResolver.CanAct(action.Source, _rules)) continue;
                await action.ExecuteAsync();
            }

            // End-of-turn: Burn and Poison deal 1/16 max HP (Gen 1–5); fraction is rules-governed
            StatusResolver.ApplyEndOfTurnDamage(PlayerCreature, _rules);
            StatusResolver.ApplyEndOfTurnDamage(EnemyCreature,  _rules);

            if (!EnemyCreature.IsAlive())
            {
                Console.WriteLine($"{EnemyCreature.Name} fainted!");
                break;
            }
            if (!PlayerCreature.IsAlive())
            {
                Console.WriteLine($"{PlayerCreature.Name} fainted!");
                break;
            }

            Console.WriteLine("\nPress any key for the next round...");
            if (!Console.IsInputRedirected)
                Console.ReadKey();
        }
    }
}
