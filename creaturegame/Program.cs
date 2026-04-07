using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creature;
using creaturegame.Creature.Creatures;
using creaturegame.DB;

namespace creaturegame;

class Program
{
    static async Task Main(string[] args)
    {
        var context = new GameDbContext();
        var attackService = new AttackService(context);

        var creature1 = new creaturegame.Creature.Creature("Tommy")
        {
            BaseHP = 45,
            BaseAttack = 49,
            BaseDefense = 49,
            BaseSpecial = 65,
            BaseSpeed = 45,
            Type1 = DamageType.Grass,
            Type2 = DamageType.Poison,
            Level = 50
        };
        creature1.CalculateStats();

        var creature2 = new Dragon("Jimmy");

        // Give Tommy the default move
        await attackService.GiveDefaultMoveAsync(creature1);
        
        // Give Jimmy a random move
        await attackService.GiveRandomMoveAsync(creature2);

        Console.WriteLine("--- Creature 1 Info ---");
        creature1.DisplayInfo();
        Console.WriteLine("\n--- Creature 2 Info ---");
        creature2.DisplayInfo();
        
        var battle = new Battle(creature1, creature2);
        await battle.StartFightAsync();
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}