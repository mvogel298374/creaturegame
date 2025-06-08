using creaturegame.Combat;
using creaturegame.Creature.Creatures;

namespace creaturegame;

class Program
{
    static void Main(string[] args)
    {
        var creature1 = new Creature.Creature("Tommy");
        var creature2 = new Dragon("Jimmy");
        creature1.DisplayInfo();

        var newbattle = new Battle(creature1, creature2);
        newbattle.StartFight();
        
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
} 