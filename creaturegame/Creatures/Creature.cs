using creaturegame.Attacks;
using creaturegame.Combat;

namespace creaturegame.Creatures;

public class Creature
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The species' own display name (e.g. "PIKACHU"), independent of <see cref="Name"/> — which may
    /// now be a player-chosen nickname (<see cref="NicknameRules"/>). Set to the same value as <see cref="Name"/>
    /// at construction, so every caller that never nicknames a creature sees no behavior change; advanced by
    /// <see cref="EvolveTo"/> to the new form's name. Two consumers: the CHECK POKEMON overview shows it
    /// alongside a nickname, and <see cref="EvolveTo"/> uses <c>Name == SpeciesName</c> to tell "still the
    /// species default" from "a nickname to preserve."</summary>
    public string SpeciesName { get; set; } = string.Empty;

    public int Level { get; set; } = 1;
    public Attributes Attributes { get; set; } = new Attributes();

    // The moveset is mutated only by the battle thread, but a web request thread may enumerate it
    // concurrently for the CHECK POKEMON overview (PlayerOverviewDto.From). A plain List can throw
    // "Collection was modified" if a structural change races that read, so every mutation here is
    // copy-on-write: build a new list and swing the reference (assignment is atomic) rather than
    // mutating in place. A concurrent reader then safely enumerates the prior list — worst case it
    // sees the pre-mutation moveset for one tick, the same staleness already accepted for Bag/scalars.
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
            MoveSet = [.. MoveSet, new PokemonAttack(attack)];
            return true;
        }
        return false;
    }

    /// <summary>
    /// The creature's level-up learnset — the moves it gains at given levels, resolved to real
    /// <see cref="Attack"/>s. Part of the permanent half (not reset between battles), so a move learned
    /// mid-run persists into the next encounter. Only the player populates this; enemies never level up.
    /// </summary>
    public IReadOnlyList<LearnsetMove> Learnset { get; set; } = [];

    /// <summary>
    /// The moves this creature learns exactly at <paramref name="level"/>, excluding any it already knows.
    /// Consulted by the battle loop after each level gained to drive auto-learn / replacement prompts.
    /// </summary>
    public IEnumerable<Attack> MovesLearnedAtLevel(int level) =>
        Learnset
            .Where(e => e.Level == level && MoveSet.All(m => m.Base.Id != e.Move.Id))
            .Select(e => e.Move);

    /// <summary>
    /// Overwrites the move in <paramref name="slot"/> (0–3) with a freshly-PP'd <paramref name="move"/> —
    /// the level-up "forget a move to make room" path. No-op if the slot is out of range.
    /// </summary>
    public void ReplaceMove(int slot, Attack move)
    {
        if (slot >= 0 && slot < MoveSet.Count)
        {
            var updated = new List<PokemonAttack>(MoveSet);
            updated[slot] = new PokemonAttack(move);
            MoveSet = updated;
        }
    }

    /// <summary>
    /// Replaces the whole moveset in one atomic reference swap — the copy-on-write entry point for effects
    /// (e.g. Transform) that rebuild the moveset from outside <see cref="Creature"/>. See the
    /// <see cref="MoveSet"/> field comment for why mutations swing the reference rather than edit in place.
    /// </summary>
    internal void SetMoveSet(IEnumerable<PokemonAttack> moves) => MoveSet = [.. moves];

    public DamageType? Type1 { get; set; }
    public DamageType? Type2 { get; set; }

    /// <summary>
    /// The creature's types as a list — 0, 1 or 2 entries, in slot order, nulls dropped. The one place that
    /// knows a creature's typing is the <see cref="Type1"/>/<see cref="Type2"/> pair, for callers that want to
    /// iterate the typing rather than test a specific slot (type-chart sweeps, wire projections).
    /// </summary>
    public IReadOnlyList<DamageType> Types
    {
        get
        {
            var types = new List<DamageType>(2);
            if (Type1 is { } t1)
                types.Add(t1);
            if (Type2 is { } t2)
                types.Add(t2);
            return types;
        }
    }

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
    /// Awards the Stat Exp (Gen 1) / EVs (later gens) gained for defeating <paramref name="defeated"/>,
    /// delegating the gen-variable gain rule and cap to <see cref="StatCalculator"/>. Like Gen 1, this only
    /// accumulates — the gain is realized into actual stats on the next <see cref="CalculateStats"/> (a
    /// level-up), never mid-level. Call it before the win's level-up loop so a level gained this battle picks
    /// the new training up.
    /// </summary>
    public void GainStatExp(Creature defeated) => StatCalculator.AwardStatExp(this, defeated);

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

    // DVs (Determinant Values) — Gen 1/2's per-individual genetic roll, each 0–15. The HP DV is NOT stored
    // independently: it's derived from the low bits of the other four (see Gen1StatCalculator.RandomiseDvs).
    // These are the ancestor of Gen 3+ IVs (0–31, six independent values incl. an own HP IV, and Special
    // split into SpAtk/SpDef DV→IV). When a generation changes that shape it does so in IStatCalculator, not
    // here — these fields stay the storage; see IStatCalculator's XML doc + TODO.md "Multi-Generation".
    public int DvHP { get; set; } = 0;
    public int DvAttack { get; set; } = 0;
    public int DvDefense { get; set; } = 0;
    public int DvSpecial { get; set; } = 0;
    public int DvSpeed { get; set; } = 0;

    // Stat Exp — Gen 1/2's training accumulator (the precursor to Gen 3+ EVs), each 0–65535. Grown by
    // GainStatExp on a win and realized into actual stats only on the next CalculateStats (a level-up). The
    // gain rule + cap are gen-variable and live on IStatCalculator.AwardStatExp, not here.
    public int ExpHP { get; set; } = 0;
    public int ExpAttack { get; set; } = 0;
    public int ExpDefense { get; set; } = 0;
    public int ExpSpecial { get; set; } = 0;
    public int ExpSpeed { get; set; } = 0;

    // Owned by BattleState; reset by assigning a fresh instance — no delegating facade, by design
    // (STATE_MODEL.md §3/§4.2/§4.3).
    public BattleState Battle { get; set; } = new();

    /// <summary>
    /// True when at least one move can be chosen this turn: it has PP and isn't Disabled. When
    /// false the creature must Struggle (out of PP, or its only PP-bearing move is disabled).
    /// </summary>
    public bool CanSelectAnyMove =>
        MoveSet.Any(m => m.PowerPointsCurrent > 0 && m != Battle.DisabledMove);

    /// <summary>Undoes a transient Mimic move-swap. Safe to call when no Mimic is active. Reverts before
    /// any reset, not just at battle end — STATE_MODEL.md §2.</summary>
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

    /// <summary>Undoes a Transform/Conversion identity change. Current HP/MaxHP are preserved. Safe to
    /// call when no mutation is active. Reverts before any reset, not just at battle end —
    /// STATE_MODEL.md §2.</summary>
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
        MoveSet = [.. snap.MoveSet];
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

    /// <summary>Haze's own reset — a deliberately narrow, field-by-field clear, NOT a
    /// <see cref="ResetBattleState"/> wholesale replace. Full field list + Gen 1 rationale:
    /// STATE_MODEL.md §2. <paramref name="preserveMajorStatus"/> is true for the user's own side, false for
    /// the target's (Haze cures only the target's status).</summary>
    public void ResetForHaze(bool preserveMajorStatus)
    {
        Battle.Stages = new StatStages();
        Battle.ConfusedTurns = 0;
        Battle.DisabledMove = null;
        Battle.DisableTurnsRemaining = 0;
        Battle.HasMist = false;
        Battle.HasFocusEnergy = false;
        Battle.HasLeechSeed = false;
        Battle.HasReflect = false;
        Battle.HasLightScreen = false;

        if (!preserveMajorStatus)
        {
            // A freshly-cured Sleep/Freeze target still can't act this turn — StatusResolver.CanAct.
            if (Battle.Status is StatusCondition.Sleep or StatusCondition.Freeze)
                Battle.HazeSuppressedStatus = Battle.Status;

            Battle.Status = StatusCondition.None;
            Battle.SleepTurns = 0;
            Battle.ToxicCounter = 1;
            return;
        }

        // Toxic still degrades to a regular Poison for the user too (pokered clears the "bad poison"
        // bit for both sides) — only the ToxicCounter escalation stops, not the Poison itself.
        if (Battle.Status == StatusCondition.BadPoison)
        {
            Battle.Status = StatusCondition.Poison;
            Battle.ToxicCounter = 1;
        }
    }

    /// <summary>This creature's major status out of battle — the multi-creature carry model
    /// (STATE_MODEL.md §2). Permanent half, unlike the transient <see cref="Battle"/> status. Null = none.
    /// </summary>
    public CarriedStatus? CarriedStatus { get; set; }

    /// <summary>The Gen 1 Poké Center heal (HP + PP + status, unconditional, free) — generation-invariant, so
    /// ordinary engine logic, not a seam.</summary>
    public void FullHeal()
    {
        Attributes.HP = Attributes.MaxHP;
        foreach (var move in MoveSet)
            move.PowerPointsCurrent = move.Base.PowerPointsMax;
        Battle.Status = StatusCondition.None;
        Battle.SleepTurns = 0;
        Battle.ToxicCounter = 1;
        CarriedStatus = null; // clears the persisted out-of-battle status too — STATE_MODEL.md §2
    }

    public IStatCalculator StatCalculator { get; set; } = Gen1StatCalculator.Instance;

    private bool _statsInitialized;

    private Creature()
    {
        // Ordinary individual values; enemy strength tiers re-roll at a chosen DvQuality after construction.
        StatCalculator.RandomiseDvs(this, DvQuality.Average);
    }

    public Creature(string name)
        : this()
    {
        Name = name;
        SpeciesName = name;
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

    /// <summary>
    /// Evolves this creature into <paramref name="newForm"/>. Adopts the evolved species' base stats, types,
    /// growth rate, base-experience and id, then recomputes stats in place via <see cref="InitializeFromSpecies"/>.
    /// The individual half carries over untouched — DVs, Stat Exp, Level, Experience, PP and the moveset are
    /// all kept (Gen 1: evolution preserves your IVs/training and current moves). Because
    /// <see cref="CalculateStats"/> heals only the max-HP delta on an increase, current HP rises by exactly
    /// the HP gained and damage already taken is preserved — the authentic Gen 1 behaviour.
    /// <para>
    /// Evolution itself grants no moves; learning the evolved form's level-up moves is the caller's job (the
    /// run loop assigns the new <see cref="Learnset"/> and drives the same auto-learn / replacement prompt as
    /// a level-up). The name is upper-cased to match how creatures are named at construction.
    /// </para>
    /// <para>
    /// <see cref="Name"/> only advances if it was still the species default (<c>Name == SpeciesName</c>) — a
    /// player nickname (<see cref="NicknameRules"/>) is preserved across evolution, matching Gen 1.
    /// <see cref="SpeciesName"/> itself always advances.
    /// </para>
    /// </summary>
    public void EvolveTo(DB.PokemonSpecies newForm)
    {
        if (Name == SpeciesName)
            Name = newForm.Name.ToUpper();
        SpeciesName = newForm.Name.ToUpper();
        InitializeFromSpecies(newForm);
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
