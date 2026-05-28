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
    public int SpeciesBaseExperience { get; set; }

    public int Experience { get; set; } = 0;
    public const int MaxLevel = 100;

    public void GainExperience(int amount)
    {
        Experience += amount;
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
            CalculateStats();
        }
    }

    public int CalculateExperienceForLevel(int level)
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
    public int ToxicCounter { get; set; } = 1;

    // Stat stages (reset each battle; [-6, +6] per stat)
    public StatStages Stages { get; set; } = new StatStages();

    // Per-battle transient state — all cleared by ResetBattleState()
    public bool IsRecharging { get; set; } = false;
    public bool IsFlinched   { get; set; } = false;
    public bool HasLeechSeed { get; set; } = false;
    public int  BindingTurnsRemaining { get; set; } = 0;
    public bool IsTwoTurnCharging { get; set; } = false;
    public PokemonAttack? ChargingMove { get; set; } = null;

    /// <summary>
    /// Clears all transient in-battle state. Called by Battle at the start of each
    /// fight so the same Creature instance can be reused across multiple battles.
    /// </summary>
    public void ResetBattleState()
    {
        Status       = StatusCondition.None;
        SleepTurns   = 0;
        ConfusedTurns = 0;
        ToxicCounter  = 1;
        Stages.Clear();
        IsRecharging          = false;
        IsFlinched            = false;
        HasLeechSeed          = false;
        BindingTurnsRemaining = 0;
        IsTwoTurnCharging     = false;
        ChargingMove          = null;
    }

    public IStatCalculator StatCalculator { get; set; } = Gen1StatCalculator.Instance;

    private bool _statsInitialized;

    private Creature()
    {
        StatCalculator.RandomiseDvs(this);
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
        SpeciesBaseExperience = species.BaseExperience;
        CalculateStats();
    }

    public void CalculateStats()
    {
        int newMaxHP = StatCalculator.CalculateHP(BaseHP, DvHP, ExpHP, Level);
        int oldMaxHP = Attributes.MaxHP;

        Attributes.MaxHP   = newMaxHP;
        Attributes.Attack  = StatCalculator.CalculateOtherStat(BaseAttack,  DvAttack,  ExpAttack,  Level);
        Attributes.Defense = StatCalculator.CalculateOtherStat(BaseDefense, DvDefense, ExpDefense, Level);
        Attributes.Special = StatCalculator.CalculateOtherStat(BaseSpecial, DvSpecial, ExpSpecial, Level);
        Attributes.Speed   = StatCalculator.CalculateOtherStat(BaseSpeed,   DvSpeed,   ExpSpeed,   Level);

        if (!_statsInitialized)
        {
            // First call — set HP to full.
            Attributes.HP    = newMaxHP;
            _statsInitialized = true;
        }
        else if (newMaxHP > oldMaxHP)
        {
            // MaxHP grew (e.g. level-up) — heal the delta, preserving damage taken.
            Attributes.ReceiveHealing(newMaxHP - oldMaxHP);
        }
        else
        {
            // MaxHP unchanged or shrank — clamp current HP to new ceiling.
            Attributes.HP = Math.Min(Attributes.HP, newMaxHP);
        }
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