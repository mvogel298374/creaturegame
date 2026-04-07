using creaturegame.Attacks;
using creaturegame.Creature.Traits;
using creaturegame.Creature.Parts;

namespace creaturegame.Creature;

public class Creature
{
    private BodyPart Parts { get; set; } = new BodyPart(new Body(), new Brain());
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public Attributes Attributes { get; set; } = new Attributes();
    private List<Trait> Traits { get; set; } = [];
    public List<Attack> MoveSet { get; private set; } = [];

    public bool AddAttack(Attack attack)
    {
        if (MoveSet.Count < 4 && !MoveSet.Any(m => m.Id == attack.Id))
        {
            MoveSet.Add(attack);
            return true;
        }
        return false;
    }

    public DamageType? Type1 { get; set; }
    public DamageType? Type2 { get; set; }

    public int Experience { get; set; } = 0;
    
    // Base Stats
    public int BaseHP { get; set; }
    public int BaseAttack { get; set; }
    public int BaseDefense { get; set; }
    public int BaseSpecial { get; set; }
    public int BaseSpeed { get; set; }

    // DVs (Individual Values in Gen 1: 0-15)
    public int DvHP { get; set; } = 0;
    public int DvAttack { get; set; } = 0;
    public int DvDefense { get; set; } = 0;
    public int DvSpecial { get; set; } = 0;
    public int DvSpeed { get; set; } = 0;

    // Stat Exp (Effort Values in Gen 1: 0-65535)
    public int ExpHP { get; set; } = 0;
    public int ExpAttack { get; set; } = 0;
    public int ExpDefense { get; set; } = 0;
    public int ExpSpecial { get; set; } = 0;
    public int ExpSpeed { get; set; } = 0;
    
    // Status
    public StatusCondition Status { get; set; } = StatusCondition.None;
    public int SleepTurns { get; set; } = 0;

    protected Creature()
    {
        GenerateRandomDVs();
    }

    public Creature(string name) : this()
    {
        Name = name;
        Traits = [new Trait(TraitType.Ability, "Heat", "Gives Heat"), new Trait(TraitType.Flaw, "Weak", "is weak")];
    }

    public void InitializeFromSpecies(DB.PokemonSpecies species)
    {
        BaseHP = species.BaseHP;
        BaseAttack = species.BaseAttack;
        BaseDefense = species.BaseDefense;
        BaseSpecial = species.BaseSpecial;
        BaseSpeed = species.BaseSpeed;
        Type1 = species.Type1;
        Type2 = species.Type2;
        CalculateStats();
    }

    private void GenerateRandomDVs()
    {
        DvAttack = Random.Shared.Next(16);
        DvDefense = Random.Shared.Next(16);
        DvSpecial = Random.Shared.Next(16);
        DvSpeed = Random.Shared.Next(16);
        // HP DV is calculated from the lowest bit of the other four DVs
        // Bit 0 of Attack, Defense, Speed, Special (order: ATK, DEF, SPD, SPEC)
        DvHP = ((DvAttack & 1) << 3) | ((DvDefense & 1) << 2) | ((DvSpeed & 1) << 1) | (DvSpecial & 1);
    }

    public void CalculateStats()
    {
        Attributes.HP = CalculateHP(BaseHP, DvHP, ExpHP, Level);
        Attributes.MaxHP = Attributes.HP;
        Attributes.Attack = CalculateOtherStat(BaseAttack, DvAttack, ExpAttack, Level);
        Attributes.Defense = CalculateOtherStat(BaseDefense, DvDefense, ExpDefense, Level);
        Attributes.Special = CalculateOtherStat(BaseSpecial, DvSpecial, ExpSpecial, Level);
        Attributes.Speed = CalculateOtherStat(BaseSpeed, DvSpeed, ExpSpeed, Level);
    }

    private static int CalculateHP(int baseStat, int dv, int exp, int level)
    {
        // Gen 1 HP Formula: ( ( (Base + DV) * 2 + (sqrt(Exp)/4) ) * Level ) / 100 + Level + 10
        double expBonus = Math.Floor(Math.Sqrt(exp)) / 4;
        return (int)Math.Floor(((baseStat + dv) * 2 + expBonus) * level / 100) + level + 10;
    }

    private static int CalculateOtherStat(int baseStat, int dv, int exp, int level)
    {
        // Gen 1 Other Stats Formula: ( ( (Base + DV) * 2 + (sqrt(Exp)/4) ) * Level ) / 100 + 5
        double expBonus = Math.Floor(Math.Sqrt(exp)) / 4;
        return (int)Math.Floor(((baseStat + dv) * 2 + expBonus) * level / 100) + 5;
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
        foreach (Attack attack in MoveSet)
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