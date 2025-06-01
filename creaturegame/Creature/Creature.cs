using System.ComponentModel.Design;
using System.Xml.Serialization;
using creaturegame.Attacks;
using creaturegame.Creature.Traits;
using creaturegame.Creature.Parts;

namespace creaturegame.Creature;

public class Creature
{
    public BodyPart Parts { get; set; }
    public string Name { get; set; }
    public int Level { get; set; } = 1;
    public Attributes Attributes { get; set; } = new Attributes();
    public List<Trait> Traits { get; set; } = [];
    public List<Attack> Attacks { get; set; } = [new Attack("Basic Attack", "just a basic attack")];
    public Creature(Parts.BodyPart bodyPart, string name, Attributes attributes)
    {
        Parts = bodyPart;
        Name = name;
        
    }

    public Creature(string name)
    {
        Parts = new BodyPart(new Body(), new Brain());
        Name = name;
        Traits = [new Trait(TraitType.Ability, "Heat", "Gives Heat"), new Trait(TraitType.Flaw, "Weak", "is weak")];
    }

    protected Creature()
    {
        throw new NotImplementedException();
    }


    public void DisplayInfo()
    {
        Console.WriteLine($"Name: {Name}, Level: {Level}");
        Console.WriteLine($"Attributes: {Attributes.ToString()}");
        Console.WriteLine($"Parts: {Parts.ToString()}");
        DisplayTraits();
        DisplayAttacks();
    }

    private void DisplayTraits()
    {
        int index = 1;
        foreach (Trait trait in Traits)
        {
            Console.WriteLine($"Trait #{index}: {trait.ToString()}");
            index++;
        }
    }

    private void DisplayAttacks()
    {
        int index = 1;
        foreach (Attack attack in Attacks)
        {
            Console.WriteLine($"Attack #{index}: {attack.ToString()}");
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