using creaturegame;

namespace creaturegame.Combat;

public class Battle
{
    private Creature.Creature PlayerCreature { get; }
    private Creature.Creature EnemyCreature { get; }
    private readonly ITypeChart _typeChart;

    /// <summary>
    /// Creates a battle. Pass the generation-appropriate <paramref name="typeChart"/> to control type effectiveness rules.
    /// </summary>
    public Battle(Creature.Creature player, Creature.Creature enemy, ITypeChart typeChart)
    {
        PlayerCreature = player;
        EnemyCreature = enemy;
        _typeChart = typeChart;
    }

    public async Task StartFightAsync()
    {
        Console.WriteLine($"A wild {EnemyCreature.Name} appeared!");
        Console.WriteLine($"Go! {PlayerCreature.Name}!");

        while (PlayerCreature.IsAlive() && EnemyCreature.IsAlive())
        {
            Console.WriteLine($"\n{PlayerCreature.Name}: {PlayerCreature.Attributes.HP}/{PlayerCreature.Attributes.MaxHP} HP");
            Console.WriteLine($"{EnemyCreature.Name}: {EnemyCreature.Attributes.HP}/{EnemyCreature.Attributes.MaxHP} HP");

            // For now, always pick first move for simplicity in this demo
            var playerMove = PlayerCreature.MoveSet.Count > 0 ? PlayerCreature.MoveSet[0] : null;
            var enemyMove = EnemyCreature.MoveSet.Count > 0 ? EnemyCreature.MoveSet[0] : null;

            if (playerMove == null || enemyMove == null) break;

            var playerAction = new AttackAction(PlayerCreature, EnemyCreature, playerMove, _typeChart);
            var enemyAction = new AttackAction(EnemyCreature, PlayerCreature, enemyMove, _typeChart);

            // Turn Resolution:
            // 1. Priority
            // 2. Speed
            // 3. Random tie-breaker
            
            var turnQueue = new List<IBattleAction> { playerAction, enemyAction };
            
            turnQueue = turnQueue.OrderByDescending(a => a.Priority)
                                 .ThenByDescending(a => a.Source.Attributes.Speed)
                                 .ThenBy(a => Random.Shared.Next())
                                 .ToList();

            foreach (var action in turnQueue)
            {
                if (action.Source.IsAlive() && (action as AttackAction)?.Target.IsAlive() == true)
                {
                    await action.ExecuteAsync();
                }
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