using creaturegame;

namespace creaturegame.Combat;

public class Battle
{
    private Creature.Creature PlayerCreature { get; set; }
    private Creature.Creature EnemyCreature { get; set; }

    public Battle(Creature.Creature attacker, Creature.Creature defender)
    {
        PlayerCreature = attacker;
        EnemyCreature = defender;
    }

    public void StartFight()
    {
        Console.WriteLine("Battle starts!");

        Creature.Creature attacker;
        Creature.Creature defender;
        
        if (PlayerCreature.Attributes.GetSpeed() > EnemyCreature.Attributes.GetSpeed())
        {
            attacker = PlayerCreature;
            defender = EnemyCreature;    
        }
        else if (PlayerCreature.Attributes.GetSpeed() < EnemyCreature.Attributes.GetSpeed())
        {
            attacker = EnemyCreature;
            defender = PlayerCreature; 
        }
        else
        {
            var random = Random.Shared.NextInt64(1,2);
            if (random == 1)
            {
                attacker = PlayerCreature;
                defender = EnemyCreature;
            }
            else
            {
                attacker = EnemyCreature;
                defender = PlayerCreature; 
            }
        }
        
        while (PlayerCreature.IsAlive() && EnemyCreature.IsAlive())
        {
            // TODO make attack choosable & pause for input here
            attacker.Attack(defender, attacker.MoveSet[0]);

            Console.WriteLine(defender.IsAlive()
                ? $"{defender.Name} now has {defender.Attributes.GetCurrentHealth()} health remaining."
                : $"{defender.Name} has fainted. {attacker.Name} wins the combat!");

            // Swap roles for the next turn. A simple swap to alternate between creatures.
            (attacker, defender) = (defender, attacker);

            // (Optional) Pause between turns for readability.
            Console.WriteLine("Press any key for the next round...");
            Console.ReadKey();
        }
    }
}