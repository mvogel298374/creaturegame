using creaturegame.Attacks;

namespace creaturegame.Creatures;

public class Creature
{
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public Attributes Attributes { get; set; } = new Attributes();
    public List<PokemonAttack> MoveSet { get; private set; } = [];

    // Struggle is a system-level fallback — never exposed publicly.
    private readonly Attack _struggle = new Attack("Struggle", "An attack that also hurts the user.") { BaseDamage = 50, Accuracy = 100, DamageType = DamageType.Normal };
    public bool IsOutOfPP => MoveSet.Count > 0 && MoveSet.All(m => m.PowerPointsCurrent <= 0);
    internal Attack Struggle => _struggle;
    internal PokemonAttack? GetAvailableMove() => MoveSet.FirstOrDefault(m => m.PowerPointsCurrent > 0);
    public bool AddAttack(Attack attack)
    {
        if (MoveSet.Count < 4 && !MoveSet.Any(m => m.Base.Id == attack.Id))
        {
            MoveSet.Add(new PokemonAttack(attack));
            return true;
        }
        return false;
    }

    public DamageType? Type1 { get; set; }
    public DamageType? Type2 { get; set; }
    public GrowthRate GrowthRate { get; set; } = GrowthRate.MediumFast;

    public int Experience { get; set; } = 0;
    public const int MaxLevel = 100;

    public void GainExperience(int amount)
    {
        Experience += amount;
        // Simplified level up logic as per requirement:
        // "lets not worry about WHEN a pokemon levels up"
        // But we still need a way to level up.
        // I will implement a basic Experience to Level mapping or just a manual LevelUp.
        // Given the request, I'll add a manual LevelUp method and maybe a simple threshold here.
        
        int nextLevelExperience = CalculateExperienceForLevel(Level + 1);
        while (Experience >= nextLevelExperience && Level < MaxLevel)
        {
            LevelUp();
            nextLevelExperience = CalculateExperienceForLevel(Level + 1);
        }
    }

    public void LevelUp()
    {
        if (Level < MaxLevel)
        {
            Level++;
            int oldMaxHp = Attributes.MaxHP;
            CalculateStats();
            
            // Heal by the amount MaxHP increased
            int hpIncrease = Attributes.MaxHP - oldMaxHp;
            if (hpIncrease > 0)
            {
                Attributes.ReceiveHealing(hpIncrease);
            }
            
            Console.WriteLine($"{Name} leveled up to {Level}!");
        }
    }

    private int CalculateExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        double n = level;
        return GrowthRate switch
        {
            GrowthRate.Fast => (int)(0.8 * Math.Pow(n, 3)),
            GrowthRate.MediumFast => (int)Math.Pow(n, 3),
            GrowthRate.MediumSlow => (int)(1.2 * Math.Pow(n, 3) - 15 * Math.Pow(n, 2) + 100 * n - 140),
            GrowthRate.Slow => (int)(1.25 * Math.Pow(n, 3)),
            _ => (int)Math.Pow(n, 3)
        };
    }
    
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
    public int ConfusedTurns { get; set; } = 0;

    private Creature()
    {
        GenerateRandomDVs();
    }

    public Creature(string name) : this()
    {
        Name = name;
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
        GrowthRate = species.GrowthRate;
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
        DisplayAttacks();
    }

    private void DisplayAttacks()
    {
        int index = 1;
        foreach (PokemonAttack attack in MoveSet)
        {
            Console.WriteLine($"Attack #{index}: {attack.Base} PP:{attack.PowerPointsCurrent}/{attack.Base.PowerPointsMax}");
            index++;
        }
    }
    
    public bool IsAlive() => Attributes.HP > 0;
    
}