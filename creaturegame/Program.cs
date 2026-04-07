using creaturegame.Combat;
using creaturegame.Creature.Creatures;
using creaturegame.DB;

namespace creaturegame;

class Program
{
    static async Task Main(string[] args)
    {
        var context = new GameDbContext();
        var attackService = new AttackService(context);
        var attackSeeder = new AttackSeeder(attackService);

        // Ensure we have some data in the DB
        await attackSeeder.SeedTackleAsync();

        var creature1 = new Creature.Creature("Tommy");
        var creature2 = new Dragon("Jimmy");

        // Give Tommy the default move
        await attackSeeder.GiveDefaultMoveAsync(creature1);
        
        // Give Jimmy a random move
        await attackSeeder.GiveRandomMoveAsync(creature2);

        Console.WriteLine("--- Creature 1 Info ---");
        creature1.DisplayInfo();
        Console.WriteLine("\n--- Creature 2 Info ---");
        creature2.DisplayInfo();
        
        //var newbattle = new Battle(creature1, creature2);
        //newbattle.StartFight();
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}  