using creaturegame.Combat;

namespace creaturegame;

class Program
{
    static void Main(string[] args)
    {
        var creature1 = new Creature.Creature("Tommy");
        var creature2 = new Creature.Creature("Jimmy");
        creature1.DisplayInfo();

        var newbattle = new Battle(creature1, creature2);
        
        
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
} 