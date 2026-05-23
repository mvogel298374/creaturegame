using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public class Battle
{
    private Creature PlayerCreature { get; }
    private Creature EnemyCreature  { get; }
    private readonly ITypeChart   _typeChart;
    private readonly IBattleInput _playerInput;
    private readonly IBattleInput _enemyInput;
    private int _turnNumber;

    /// <summary>
    /// Creates a battle.
    /// Pass the generation-appropriate <paramref name="typeChart"/> to control type
    /// effectiveness rules, and one <see cref="IBattleInput"/> per side to control
    /// move selection. Use <see cref="AutoSelectInput.Instance"/> for sides that are
    /// not yet wired to a real input source.
    /// </summary>
    public Battle(Creature player, Creature enemy, ITypeChart typeChart,
                  IBattleInput playerInput, IBattleInput enemyInput)
    {
        PlayerCreature = player;
        EnemyCreature  = enemy;
        _typeChart     = typeChart;
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

            var playerAction = new AttackAction(PlayerCreature, EnemyCreature, playerMove, _typeChart);
            var enemyAction  = new AttackAction(EnemyCreature, PlayerCreature, enemyMove,  _typeChart);

            // Turn resolution: Priority → Speed → random tie-breaker
            var turnQueue = new List<IBattleAction> { playerAction, enemyAction };

            turnQueue = turnQueue
                .OrderByDescending(a => a.Priority)
                .ThenByDescending(a => a.Source.Attributes.Speed)
                .ThenBy(_ => Random.Shared.Next())
                .ToList();

            foreach (var action in turnQueue)
            {
                if (action.Source.IsAlive() && (action as AttackAction)?.Target.IsAlive() == true)
                    await action.ExecuteAsync();
            }

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
            Console.ReadKey();
        }
    }
}
