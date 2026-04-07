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
        Console.WriteLine("=== Gen 1 Pokemon Battle Simulator Scaffold ===");
        
        var context = new GameDbContext();
        var attackService = new AttackService(context);

        // Define Bulbasaur (Tommy)
        var creature1 = new creaturegame.Creature.Creature("Tommy (Bulbasaur)")
        {
            BaseHP = 45,
            BaseAttack = 49,
            BaseDefense = 49,
            BaseSpecial = 65,
            BaseSpeed = 45,
            Type1 = DamageType.Grass,
            Type2 = DamageType.Poison,
            Level = 50,
            DvAttack = 15, DvDefense = 15, DvSpecial = 15, DvSpeed = 15, DvHP = 15
        };
        creature1.CalculateStats();

        // Define Dragonite (Jimmy)
        var creature2 = new Dragon("Jimmy (Dragonite)")
        {
            Level = 50,
            DvAttack = 15, DvDefense = 15, DvSpecial = 15, DvSpeed = 15, DvHP = 15
        };
        creature2.CalculateStats();

        // Setup Moves
        var tackle = await attackService.GetAttackByNameAsync("tackle") ?? new Attack("Tackle", "Standard physical move") { BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical };
        var hyperBeam = await attackService.GetAttackByNameAsync("hyper-beam") ?? new Attack("Hyper Beam", "Powerful special move") { BaseDamage = 150, Accuracy = 90, AttackType = AttackType.Special, DamageType = DamageType.Normal };

        creature1.AddAttack(tackle);
        creature2.AddAttack(hyperBeam);

        Console.WriteLine("\n--- Battle Contestants ---");
        creature1.DisplayInfo();
        Console.WriteLine();
        creature2.DisplayInfo();
        
        Console.WriteLine("\n--- Battle Start! ---");
        var battle = new Battle(creature1, creature2);
        await battle.StartFightAsync();
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}