using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;
using DT = creaturegame.Attacks.DamageType;

namespace creaturegame.Tests.Unit;

public class DamageCalculationTests
{
    [Fact]
    public void DamageCalculationFormula()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 100;

        var move = new Attack
        {
            Name = "Tackle",
            BaseDamage = 40,
            AttackType = AttackType.Physical,
        };

        int damage = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        Assert.InRange(damage, 16, 19);
    }

    // --- STAB Tests ---

    [Fact]
    public void STAB_Type1Match_IncreasesDamage()
    {
        // All variables fixed; only STAB changes between two calls.
        // STAB worst case (1.5 × 217/255 ≈ 1.28) always exceeds non-STAB best case (1.0),
        // so a single sample is deterministically sufficient.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;
        attacker.Type1 = DamageType.Fire;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack
        {
            Name = "Ember",
            BaseDamage = 80,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Fire,
        };

        int stabDamage = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        attacker.Type1 = DamageType.Water; // no STAB on Fire move
        int nonStabDamage = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        Assert.True(
            stabDamage > nonStabDamage,
            $"STAB damage ({stabDamage}) should exceed non-STAB ({nonStabDamage})"
        );
    }

    [Fact]
    public void STAB_Type2Match_IncreasesDamage()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;
        attacker.Type1 = DamageType.Normal; // doesn't match move
        attacker.Type2 = DamageType.Fire; // matches move → STAB via Type2

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack
        {
            Name = "Ember",
            BaseDamage = 80,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Fire,
        };

        int stabDamage = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        attacker.Type2 = null; // remove Type2 STAB
        int nonStabDamage = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        Assert.True(
            stabDamage > nonStabDamage,
            $"Type2 STAB damage ({stabDamage}) should exceed non-STAB ({nonStabDamage})"
        );
    }

    // These establish the Gen 1 contract so that when Gen2BattleRules is written,
    // parallel tests can assert it returns SpAtk/SpDef instead.

    [Fact]
    public void Gen1BattleRules_GetOffensiveStat_Physical_ReturnsAttack()
    {
        var c = new Creature("Test") { Level = 50 };
        c.CalculateStats();
        c.Attributes.Attack = 120;
        c.Attributes.Special = 80;
        Assert.Equal(120, Gen1BattleRules.Instance.GetOffensiveStat(c, AttackType.Physical));
    }

    [Fact]
    public void Gen1BattleRules_GetOffensiveStat_Special_ReturnsSpecial()
    {
        var c = new Creature("Test") { Level = 50 };
        c.CalculateStats();
        c.Attributes.Attack = 120;
        c.Attributes.Special = 80;
        Assert.Equal(80, Gen1BattleRules.Instance.GetOffensiveStat(c, AttackType.Special));
    }

    [Fact]
    public void Gen1BattleRules_GetDefensiveStat_Physical_ReturnsDefense()
    {
        var c = new Creature("Test") { Level = 50 };
        c.CalculateStats();
        c.Attributes.Defense = 110;
        c.Attributes.Special = 70;
        Assert.Equal(110, Gen1BattleRules.Instance.GetDefensiveStat(c, AttackType.Physical));
    }

    [Fact]
    public void Gen1BattleRules_GetDefensiveStat_Special_ReturnsSpecial()
    {
        var c = new Creature("Test") { Level = 50 };
        c.CalculateStats();
        c.Attributes.Defense = 110;
        c.Attributes.Special = 70;
        Assert.Equal(70, Gen1BattleRules.Instance.GetDefensiveStat(c, AttackType.Special));
    }

    [Fact]
    public void DamageCalculator_UsesOffensiveStatFromRules()
    {
        // Rules that always return Attack for offensive lookups (including special moves).
        // If DamageCalculator hardcoded Attributes.Special for special moves,
        // damage would be based on Special=50 not Attack=200.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 200;
        attacker.Attributes.Special = 50;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Special = 50;

        var specialMove = new Attack
        {
            Id = 1,
            Name = "Psychic",
            BaseDamage = 90,
            Accuracy = 100,
            DamageType = DamageType.Psychic,
            AttackType = AttackType.Special,
        };

        int dmgViaAttack = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            specialMove,
            new Gen1TypeChart(),
            new AlwaysUseAttackStatRules()
        );
        int dmgViaSpecial = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            specialMove,
            new Gen1TypeChart(),
            AlwaysHitRules.Instance
        );

        Assert.True(
            dmgViaAttack > dmgViaSpecial,
            $"Damage via Attack=200 ({dmgViaAttack}) should exceed damage via Special=50 ({dmgViaSpecial})"
        );
    }

    [Fact]
    public void DamageCalculator_UsesDefensiveStatFromRules()
    {
        // Rules that always return Defense for defensive lookups (including special moves).
        // Standard Gen 1 rules would use Special=50 for a special move (low → high damage).
        // Custom rules return Defense=200 (high → lower damage).
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Special = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 200;
        defender.Attributes.Special = 50;

        var specialMove = new Attack
        {
            Id = 1,
            Name = "Psychic",
            BaseDamage = 90,
            Accuracy = 100,
            DamageType = DamageType.Psychic,
            AttackType = AttackType.Special,
        };

        int dmgVsHighDef = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            specialMove,
            new Gen1TypeChart(),
            new AlwaysUseDefenseStatRules()
        );
        int dmgVsLowDef = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            specialMove,
            new Gen1TypeChart(),
            AlwaysHitRules.Instance
        );

        Assert.True(
            dmgVsHighDef < dmgVsLowDef,
            $"Damage vs Defense=200 ({dmgVsHighDef}) should be less than damage vs Special=50 ({dmgVsLowDef})"
        );
    }

    // --- pokered's integer pipeline (core.asm CalculateDamage, AdjustDamageForMoveType, RandomizeDamage) ---

    public sealed record DamageCase(
        int Level,
        int Power,
        int Attack,
        int Defense,
        DT AttackerType,
        DT MoveType,
        DT DefenderType1,
        DT? DefenderType2,
        bool Crit,
        int Roll,
        int Expected
    );

    // Hand-computed from pokered's steps; the Grass/Ground rows pin ½× then 2× floored one at a time (53 → 26 → 52).
    public static TheoryData<DamageCase> PokeredDamage =>
        new()
        {
            new(5, 40, 30, 25, DT.Fire, DT.Normal, DT.Normal, null, false, 217, 4),
            new(5, 40, 30, 25, DT.Fire, DT.Normal, DT.Normal, null, false, 255, 5),
            new(5, 40, 30, 25, DT.Fire, DT.Normal, DT.Normal, null, true, 217, 5),
            new(5, 40, 30, 25, DT.Fire, DT.Normal, DT.Normal, null, true, 255, 7),
            new(12, 40, 33, 29, DT.Normal, DT.Normal, DT.Normal, null, false, 217, 8),
            new(12, 40, 33, 29, DT.Normal, DT.Normal, DT.Normal, null, false, 255, 10),
            new(12, 40, 33, 29, DT.Normal, DT.Normal, DT.Normal, null, true, 217, 15),
            new(12, 40, 33, 29, DT.Normal, DT.Normal, DT.Normal, null, true, 255, 18),
            new(50, 95, 120, 98, DT.Fire, DT.Fire, DT.Grass, DT.Poison, false, 217, 134),
            new(50, 95, 120, 98, DT.Fire, DT.Fire, DT.Grass, DT.Poison, false, 255, 158),
            new(50, 95, 120, 98, DT.Fire, DT.Fire, DT.Grass, DT.Poison, true, 217, 251),
            new(50, 95, 120, 98, DT.Fire, DT.Fire, DT.Grass, DT.Poison, true, 255, 296),
            new(50, 95, 120, 98, DT.Water, DT.Electric, DT.Grass, DT.Poison, false, 217, 22),
            new(50, 95, 120, 98, DT.Water, DT.Electric, DT.Grass, DT.Poison, false, 255, 26),
            new(50, 95, 120, 98, DT.Water, DT.Electric, DT.Grass, DT.Poison, true, 217, 41),
            new(50, 95, 120, 98, DT.Water, DT.Electric, DT.Grass, DT.Poison, true, 255, 49),
            new(50, 95, 120, 98, DT.Fire, DT.Water, DT.Grass, DT.Ground, false, 217, 44),
            new(50, 95, 120, 98, DT.Fire, DT.Water, DT.Grass, DT.Ground, false, 255, 52),
            new(12, 55, 40, 31, DT.Water, DT.Grass, DT.Fire, DT.Flying, false, 217, 1),
            new(12, 55, 40, 31, DT.Water, DT.Grass, DT.Fire, DT.Flying, false, 255, 2),
            new(12, 55, 40, 31, DT.Water, DT.Grass, DT.Fire, DT.Flying, true, 217, 3),
            new(12, 55, 40, 31, DT.Water, DT.Grass, DT.Fire, DT.Flying, true, 255, 4),
        };

    [Theory]
    [MemberData(nameof(PokeredDamage))]
    public void DamageFollowsPokeredsIntegerSteps(DamageCase c)
    {
        var attacker = TestCreatures.Make("A", c.Level, c.AttackerType, attack: c.Attack);
        var defender = TestCreatures.Make(
            "D",
            50,
            c.DefenderType1,
            c.DefenderType2,
            defense: c.Defense
        );

        int damage = FixedHit(
            attacker,
            defender,
            Hit(c.Power, c.MoveType),
            new FixedRollRules(c.Crit, c.Roll)
        );

        Assert.Equal(c.Expected, damage);
    }

    [Fact]
    public void ALevel5CritRaisesTheLevelTermByHalfNotTheDamageByDouble()
    {
        var attacker = TestCreatures.Make("A", 5, DamageType.Fire);
        var defender = TestCreatures.Make("D");

        int normal = FixedHit(attacker, defender, Hit(100), new FixedRollRules(false, 255));
        int crit = FixedHit(attacker, defender, Hit(100), new FixedRollRules(true, 255));

        // floor(2L/5) + 2: 4 at L5, 6 at the crit's L10, so the base goes 8 → 12, then + 2.
        Assert.Equal(10, normal);
        Assert.Equal(14, crit);
    }

    [Fact]
    public void ALevel12HitDropsTheFractionOfTwoLOverFive()
    {
        var attacker = TestCreatures.Make("A", 12, DamageType.Fire);
        var defender = TestCreatures.Make("D");

        int damage = FixedHit(attacker, defender, Hit(40), new FixedRollRules(false, 255));

        // floor(24/5) + 2 = 6, and 6 × 40 ÷ 50 = 4; unfloored, 6.8 × 40 ÷ 50 + 2 would give 7.
        Assert.Equal(6, damage);
    }

    [Fact]
    public void TheBaseDamageCapsAt997BeforeThePlusTwoAndTheRoll()
    {
        var attacker = TestCreatures.Make("A", 100, DamageType.Fire, attack: 999);
        var defender = TestCreatures.Make("D", defense: 10);

        Assert.Equal(999, FixedHit(attacker, defender, Hit(250), new FixedRollRules(true, 255)));
        Assert.Equal(850, FixedHit(attacker, defender, Hit(250), new FixedRollRules(true, 217)));
    }

    [Fact]
    public void ARulesetThatDoublesCritDamageAppliesItToTheBaseBeforeTheCap()
    {
        var attacker = TestCreatures.Make("A", 50, DamageType.Fire, attack: 120);
        var defender = TestCreatures.Make(
            "D",
            50,
            DamageType.Grass,
            DamageType.Poison,
            defense: 98
        );
        var capAttacker = TestCreatures.Make("A", 100, DamageType.Fire, attack: 999);
        var capDefender = TestCreatures.Make("D", defense: 10);
        var rules = new DamageDoublingCritRules();

        // Base 51 doubles to 102, + 2, STAB 156, ×2 Grass 312; Gen 1's doubled level gives 296.
        Assert.Equal(312, FixedHit(attacker, defender, Hit(95, DamageType.Fire), rules));
        Assert.Equal(999, FixedHit(capAttacker, capDefender, Hit(250), rules));
    }

    private static Attack Hit(int power, DamageType type = DamageType.Normal) =>
        new()
        {
            Name = "Hit",
            BaseDamage = power,
            AttackType = AttackType.Physical,
            DamageType = type,
        };

    private static int FixedHit(
        Creature attacker,
        Creature defender,
        Attack move,
        IBattleRules rules
    ) =>
        DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            Gen1TypeChart.Instance,
            rules,
            out _,
            new SeededRandomSource(0)
        );

    private sealed class FixedRollRules(bool crit, int roll) : DelegatingBattleRules
    {
        public override double GetCritChance(Creature a, Attack m) => crit ? 1.0 : 0.0;

        public override int RollDamageVariance() => roll;
    }

    private sealed class DamageDoublingCritRules : DelegatingBattleRules
    {
        public override double GetCritChance(Creature a, Attack m) => 1.0;

        public override int RollDamageVariance() => DamageVarianceDivisor;

        public override int CritLevelMultiplier => 1;

        public override int CritDamageMultiplier => 2;
    }

    // Deterministic (no variance, always-hit, no crit, flat stat mult) so the only thing that
    // moves the damage is the stat the rules select — which is what these tests assert.
    private sealed class AlwaysUseAttackStatRules : DelegatingBattleRules
    {
        public override int RollDamageVariance() => DamageVarianceDivisor;

        public override double GetStatMultiplier(int stage) => 1.0;

        public override int GetHitThreshold(int a, int b, int c) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override int GetOffensiveStat(Creature a, AttackType t) => a.Attributes.Attack; // always Attack
    }

    private sealed class AlwaysUseDefenseStatRules : DelegatingBattleRules
    {
        public override int RollDamageVariance() => DamageVarianceDivisor;

        public override double GetStatMultiplier(int stage) => 1.0;

        public override int GetHitThreshold(int a, int b, int c) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override int GetDefensiveStat(Creature d, AttackType t) => d.Attributes.Defense; // always Defense
    }
}
