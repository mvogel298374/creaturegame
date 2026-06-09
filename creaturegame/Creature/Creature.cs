using creaturegame.Attacks;

namespace creaturegame.Creatures;

public class Creature
{
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public Attributes Attributes { get; set; } = new Attributes();
    public List<PokemonAttack> MoveSet { get; private set; } = [];

    // Struggle is a system-level fallback — never exposed publicly.
    private readonly Attack _struggle = new Attack(
        "Struggle",
        "An attack that also hurts the user."
    )
    {
        BaseDamage = 50,
        Accuracy = 100,
        DamageType = DamageType.Normal,
    };
    internal Attack Struggle => _struggle;

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
    public int SpeciesId { get; set; }
    public int SpeciesBaseExperience { get; set; }

    public int Experience { get; set; } = 0;
    public const int MaxLevel = 100;

    /// <summary>
    /// XP accumulated into the current level — the numerator for the level-progress bar. At the level cap
    /// there is no next level, so the bar reads full (this equals <see cref="XpToNextLevel"/>).
    /// </summary>
    public int XpThisLevel =>
        Level >= MaxLevel ? 1 : Experience - CalculateExperienceForLevel(Level);

    /// <summary>
    /// Total XP that fills the current level's bar (the denominator) — the span from this level to the next.
    /// NOT the remaining amount; <see cref="XpThisLevel"/> of this much means a level-up. At the level cap
    /// this is 1 (with <see cref="XpThisLevel"/> == 1) so the bar renders full instead of dividing by a span
    /// into a non-existent level.
    /// </summary>
    public int XpToNextLevel =>
        Level >= MaxLevel
            ? 1
            : CalculateExperienceForLevel(Level + 1) - CalculateExperienceForLevel(Level);

    /// <summary>The current stat totals — snapshot for the level-up stat-growth panel.</summary>
    public StatBlock StatSnapshot() =>
        new(
            Attributes.MaxHP,
            Attributes.Attack,
            Attributes.Defense,
            Attributes.Special,
            Attributes.Speed
        );

    /// <summary>
    /// Adds XP and levels up as many times as that allows — the convenience path for callers that don't
    /// need to observe each level individually. The battle loop instead drives <see cref="AddExperience"/>
    /// + <see cref="TryLevelUp"/> so it can emit a per-level event carrying that level's stats.
    /// </summary>
    public void GainExperience(int amount)
    {
        AddExperience(amount);
        while (TryLevelUp()) { }
    }

    /// <summary>Accumulates XP without applying any level-ups (see <see cref="TryLevelUp"/>).</summary>
    public void AddExperience(int amount) => Experience += amount;

    /// <summary>
    /// Levels up once if the accumulated XP has crossed the next threshold and the cap isn't reached.
    /// Returns true if a level was gained, so a caller can loop to step through a multi-level award and
    /// observe each level's resulting stats.
    /// </summary>
    public bool TryLevelUp()
    {
        if (Level >= MaxLevel)
            return false;
        if (Experience < CalculateExperienceForLevel(Level + 1))
            return false;
        LevelUp();
        return true;
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
        if (level <= 1)
            return 0;
        double n = level;
        return GrowthRate switch
        {
            GrowthRate.Fast => (int)(0.8 * Math.Pow(n, 3)),
            GrowthRate.MediumFast => (int)Math.Pow(n, 3),
            GrowthRate.MediumSlow => (int)(
                1.2 * Math.Pow(n, 3) - 15 * Math.Pow(n, 2) + 100 * n - 140
            ),
            GrowthRate.Slow => (int)(1.25 * Math.Pow(n, 3)),
            _ => (int)Math.Pow(n, 3),
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

    // ── Transient per-battle state ───────────────────────────────────────────
    // Owned by BattleState; reset by assigning a fresh instance (see ResetBattleState).
    // Access every per-battle field through this property (creature.Battle.X) — there is
    // deliberately no delegating facade on Creature, so a NEW per-battle field can only be
    // added to BattleState, never accidentally onto Creature. That is what makes a forgotten
    // reset structurally impossible.
    public BattleState Battle { get; set; } = new();

    /// <summary>
    /// True when at least one move can be chosen this turn: it has PP and isn't Disabled. When
    /// false the creature must Struggle (out of PP, or its only PP-bearing move is disabled).
    /// </summary>
    public bool CanSelectAnyMove =>
        MoveSet.Any(m => m.PowerPointsCurrent > 0 && m != Battle.DisabledMove);

    /// <summary>
    /// Undoes a transient Mimic move-swap, putting the original move back in the slot. Safe to call
    /// when no Mimic is active. Lives here (not just at battle end) because Mimic mutates the permanent
    /// <see cref="MoveSet"/>, so any reset of the transient state must revert it first or the copied
    /// move leaks — e.g. Haze resets battle state mid-fight.
    /// </summary>
    public void RestoreMimickedMove()
    {
        if (Battle.MimicWrapper is null || Battle.MimicOriginalBase is null)
            return;
        Battle.MimicWrapper.Base = Battle.MimicOriginalBase;
        Battle.MimicWrapper = null;
        Battle.MimicOriginalBase = null;
    }

    /// <summary>
    /// Captures the creature's pre-mutation identity (types, the four non-HP battle stats, SpeciesId,
    /// and the current moveset wrappers) so Transform / Conversion can be undone at battle end. Taken
    /// only once per battle — a second mutating move (e.g. Conversion after Transform) must NOT
    /// overwrite the snapshot, or the original is lost. Safe to call repeatedly.
    /// </summary>
    public void SnapshotIdentityForMutation()
    {
        if (Battle.OriginalIdentity is not null)
            return;
        Battle.OriginalIdentity = new IdentitySnapshot
        {
            Type1 = Type1,
            Type2 = Type2,
            SpeciesId = SpeciesId,
            Attack = Attributes.Attack,
            Defense = Attributes.Defense,
            Special = Attributes.Special,
            Speed = Attributes.Speed,
            MoveSet = [.. MoveSet],
        };
    }

    /// <summary>
    /// Undoes a Transform / Conversion identity change, restoring the original types, stats, SpeciesId,
    /// and moveset. Current HP/MaxHP are preserved (Transform never copied them). Safe to call when no
    /// mutation is active. Lives here (not just at battle end) for the same reason as the Mimic revert:
    /// the change is to the *permanent* Creature, so any reset of transient state must undo it first or
    /// the copied identity leaks — e.g. Haze resets battle state mid-fight.
    /// </summary>
    public void RestoreOriginalIdentity()
    {
        if (Battle.OriginalIdentity is not { } snap)
            return;
        Type1 = snap.Type1;
        Type2 = snap.Type2;
        SpeciesId = snap.SpeciesId;
        Attributes.Attack = snap.Attack;
        Attributes.Defense = snap.Defense;
        Attributes.Special = snap.Special;
        Attributes.Speed = snap.Speed;
        MoveSet.Clear();
        MoveSet.AddRange(snap.MoveSet);
        Battle.OriginalIdentity = null;
    }

    /// <summary>
    /// Clears all transient in-battle state by replacing it wholesale, so a newly added
    /// BattleState field can never be missed by a manual reset. Called by Battle at the
    /// start of each fight so the same Creature instance can be reused across battles.
    /// Reverts any active Mimic swap and Transform/Conversion identity change first, since
    /// those live on the permanent half of the Creature (MoveSet, types, stats).
    /// </summary>
    public void ResetBattleState()
    {
        RestoreMimickedMove();
        RestoreOriginalIdentity();
        Battle = new BattleState();
    }

    public IStatCalculator StatCalculator { get; set; } = Gen1StatCalculator.Instance;

    private bool _statsInitialized;

    private Creature()
    {
        StatCalculator.RandomiseDvs(this);
    }

    public Creature(string name)
        : this()
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
        SpeciesId = species.Id;
        SpeciesBaseExperience = species.BaseExperience;
        CalculateStats();
    }

    public void CalculateStats()
    {
        int newMaxHP = StatCalculator.CalculateHP(BaseHP, DvHP, ExpHP, Level);
        int oldMaxHP = Attributes.MaxHP;

        Attributes.MaxHP = newMaxHP;
        Attributes.Attack = StatCalculator.CalculateOtherStat(
            BaseAttack,
            DvAttack,
            ExpAttack,
            Level
        );
        Attributes.Defense = StatCalculator.CalculateOtherStat(
            BaseDefense,
            DvDefense,
            ExpDefense,
            Level
        );
        Attributes.Special = StatCalculator.CalculateOtherStat(
            BaseSpecial,
            DvSpecial,
            ExpSpecial,
            Level
        );
        Attributes.Speed = StatCalculator.CalculateOtherStat(BaseSpeed, DvSpeed, ExpSpeed, Level);

        if (!_statsInitialized)
        {
            // First call — set HP to full.
            Attributes.HP = newMaxHP;
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
            Console.WriteLine(
                $"Attack #{index}: {attack.Base} PP:{attack.PowerPointsCurrent}/{attack.Base.PowerPointsMax}"
            );
            index++;
        }
    }

    public bool IsAlive() => Attributes.HP > 0;
}
