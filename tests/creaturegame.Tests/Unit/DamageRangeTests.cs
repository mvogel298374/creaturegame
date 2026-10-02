using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>The Dev Mode damage-range estimate (docs/TODO.md — Dev Mode damage ranges): it must bracket every
/// real roll of the live formula, since it is the same formula with the variance pinned to its bounds.</summary>
public class DamageRangeTests
{
    private static readonly Gen1TypeChart Chart = new();

    private static (Creature attacker, Creature defender) Pair()
    {
        var a = TestCreatures.Make(
            "A",
            level: 50,
            attack: 120,
            special: 90,
            type1: DamageType.Fire
        );
        var d = TestCreatures.Make("D", level: 50, defense: 80, special: 70, hp: 150);
        return (a, d);
    }

    private static Attack Tackle(int power = 60) =>
        new()
        {
            Name = "Tackle",
            BaseDamage = power,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Normal,
        };

    [Fact]
    public void StandardRange_BracketsEveryRealNonCritRoll_AndBothBoundsAreReachable()
    {
        var (a, d) = Pair();
        var move = Tackle();
        var range = DamageCalculator
            .EstimateRange(a, d, move, Chart, Gen1BattleRules.Instance)!
            .Value;

        var seen = new HashSet<int>();
        for (int seed = 0; seed < 4000; seed++)
        {
            var rules = new Gen1BattleRules(new SeededRandomSource(seed));
            int dmg = DamageCalculator.CalculateDamage(a, d, move, Chart, rules, out bool crit);
            if (crit)
                continue; // the range is non-crit by design
            Assert.InRange(dmg, range.Min, range.Max);
            seen.Add(dmg);
        }

        Assert.Contains(range.Min, seen);
        Assert.Contains(range.Max, seen);
        Assert.True(range.Min < range.Max);
    }

    [Fact]
    public void StabAndTypeEffectiveness_ShiftTheRange()
    {
        var (a, d) = Pair();
        var plain = DamageCalculator
            .EstimateRange(a, d, Tackle(), Chart, Gen1BattleRules.Instance)!
            .Value;
        var fire = Tackle();
        fire.DamageType = DamageType.Fire; // STAB (attacker is Fire)

        var stab = DamageCalculator
            .EstimateRange(a, d, fire, Chart, Gen1BattleRules.Instance)!
            .Value;

        Assert.True(stab.Min > plain.Min && stab.Max > plain.Max);
    }

    [Fact]
    public void LiveStatStagesAndBurn_AreReflected()
    {
        var (a, d) = Pair();
        var move = Tackle();
        var baseline = DamageCalculator
            .EstimateRange(a, d, move, Chart, Gen1BattleRules.Instance)!
            .Value;

        a.Battle.Stages.Raise(StageStat.Attack, 2);
        var boosted = DamageCalculator
            .EstimateRange(a, d, move, Chart, Gen1BattleRules.Instance)!
            .Value;
        a.Battle.Stages.Raise(StageStat.Attack, -2);
        a.Battle.Status = StatusCondition.Burn;
        var burned = DamageCalculator
            .EstimateRange(a, d, move, Chart, Gen1BattleRules.Instance)!
            .Value;

        Assert.True(boosted.Max > baseline.Max);
        Assert.True(burned.Max < baseline.Max);
    }

    [Fact]
    public void Reflect_LowersAPhysicalRange_ButNotASpecialOne()
    {
        var (a, d) = Pair();
        var physical = Tackle();
        var special = Tackle();
        special.AttackType = AttackType.Special;
        var physBefore = DamageCalculator
            .EstimateRange(a, d, physical, Chart, Gen1BattleRules.Instance)!
            .Value;
        var specBefore = DamageCalculator
            .EstimateRange(a, d, special, Chart, Gen1BattleRules.Instance)!
            .Value;

        d.Battle.HasReflect = true;

        Assert.True(
            DamageCalculator
                .EstimateRange(a, d, physical, Chart, Gen1BattleRules.Instance)!
                .Value.Max < physBefore.Max
        );
        Assert.Equal(
            specBefore,
            DamageCalculator.EstimateRange(a, d, special, Chart, Gen1BattleRules.Instance)!.Value
        );
    }

    [Fact]
    public void SpecialDamageCategories_ReportExactOrSeamDerivedAmounts()
    {
        var (a, d) = Pair();
        var rules = Gen1BattleRules.Instance;
        Attack Make(DamageCategory c, int? fixedDmg = null) =>
            new()
            {
                Name = c.ToString(),
                DamageCategory = c,
                FixedDamageValue = fixedDmg,
            };

        Assert.Equal(
            new DamageRange(40, 40),
            DamageCalculator.EstimateRange(a, d, Make(DamageCategory.Fixed, 40), Chart, rules)
        );
        Assert.Equal(
            new DamageRange(50, 50),
            DamageCalculator.EstimateRange(a, d, Make(DamageCategory.LevelBased), Chart, rules)
        );
        Assert.Equal(
            new DamageRange(150, 150),
            DamageCalculator.EstimateRange(a, d, Make(DamageCategory.OHKO), Chart, rules)
        );
        Assert.Equal(
            new DamageRange(75, 75),
            DamageCalculator.EstimateRange(a, d, Make(DamageCategory.SuperFang), Chart, rules)
        );
        Assert.Equal(
            new DamageRange(1, 75),
            DamageCalculator.EstimateRange(a, d, Make(DamageCategory.Psywave), Chart, rules)
        ); // 1..⌊1.5×50⌋
    }

    [Fact]
    public void StatusMoves_HaveNoRange()
    {
        var (a, d) = Pair();
        var growl = new Attack { Name = "Growl", BaseDamage = 0 };

        Assert.Null(DamageCalculator.EstimateRange(a, d, growl, Chart, Gen1BattleRules.Instance));
    }

    [Fact]
    public void SelfDestruct_UsesTheRulesDefenseDivisor_SoItOutdamagesTheSamePowerStandardMove()
    {
        var (a, d) = Pair();
        var boom = Tackle(130);
        boom.DamageCategory = DamageCategory.SelfDestruct;
        var plain = Tackle(130);

        var boomRange = DamageCalculator
            .EstimateRange(a, d, boom, Chart, Gen1BattleRules.Instance)!
            .Value;
        var plainRange = DamageCalculator
            .EstimateRange(a, d, plain, Chart, Gen1BattleRules.Instance)!
            .Value;

        Assert.True(boomRange.Max > plainRange.Max);
    }

    [Fact]
    public void RangeBoundsComeFromTheRulesSeam_NotAHardcodedGen1Constant()
    {
        var (a, d) = Pair();
        // A pinned-variance ruleset (variance 1.0 .. 1.0) must collapse the range to a single value.
        var range = DamageCalculator
            .EstimateRange(a, d, Tackle(), Chart, NoVarianceNoCritHitRules.Instance)!
            .Value;

        Assert.Equal(range.Min, range.Max);
    }
}
