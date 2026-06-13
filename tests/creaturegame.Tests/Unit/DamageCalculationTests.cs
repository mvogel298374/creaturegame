using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

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

    // Deterministic (no variance, always-hit, no crit, flat stat mult) so the only thing that
    // moves the damage is the stat the rules select — which is what these tests assert.
    private sealed class AlwaysUseAttackStatRules : DelegatingBattleRules
    {
        public override double RollDamageVariance() => 1.0;

        public override double GetStatMultiplier(int stage) => 1.0;

        public override int GetHitThreshold(int a, int b, int c) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override int GetOffensiveStat(Creature a, AttackType t) => a.Attributes.Attack; // always Attack
    }

    private sealed class AlwaysUseDefenseStatRules : DelegatingBattleRules
    {
        public override double RollDamageVariance() => 1.0;

        public override double GetStatMultiplier(int stage) => 1.0;

        public override int GetHitThreshold(int a, int b, int c) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override int GetDefensiveStat(Creature d, AttackType t) => d.Attributes.Defense; // always Defense
    }
}
