using creaturegame.Attacks;
using creaturegame.Creature.Traits;
using creaturegame.Creature.Parts;

namespace creaturegame.Creature;

public class Creature
{
    private BodyPart Parts { get; set; } = new BodyPart(new Body(), new Brain());
    public string Name { get; set; } = string.Empty;
    protected int Level { get; set; } = 1;
    public Attributes Attributes { get; set; } = new Attributes();
    private List<Trait> Traits { get; set; } = [];
    public List<Attack> Attacks { get; set; } = [new Attack("Basic Attack", "just a basic attack")];

    protected Creature()
    {
    }

    public Creature(string name)
    {
        Name = name;
        Traits = [new Trait(TraitType.Ability, "Heat", "Gives Heat"), new Trait(TraitType.Flaw, "Weak", "is weak")];
    }


    public void DisplayInfo()
    {
        Console.WriteLine($"Name: {Name}, Level: {Level}");
        Console.WriteLine($"Attributes: {Attributes}");
        Console.WriteLine($"Parts: {Parts}");
        DisplayTraits();
        DisplayAttacks();
    }

    private void DisplayTraits()
    {
        int index = 1;
        foreach (Trait trait in Traits)
        {
            Console.WriteLine($"Trait #{index}: {trait}");
            index++;
        }
    }

    private void DisplayAttacks()
    {
        int index = 1;
        foreach (Attack attack in Attacks)
        {
            Console.WriteLine($"Attack #{index}: {attack}");
            index++;
        }
    }
    
    public void Attack(Creature target, Attack attack)
    {
        Console.WriteLine($"{Name} attacks {target.Name} for {attack.BaseDamage} damage!");
        target.Attributes.ReceiveDamage(attack.BaseDamage);
    }

    // Determines if the creature is still alive.
    public bool IsAlive()
    {
        var health = Attributes.GetCurrentHealth();
        return health > 0;
    }
    
}