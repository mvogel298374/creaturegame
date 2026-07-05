using creaturegame.Combat;
using creaturegame.Creatures;

namespace creaturegame.Tests.Unit;

// The roguelite run-tuning curve (RunRules), kept separate from the Gen-1 seam. These pin the *shape* the web
// run relies on: a soft, level-aware XP ramp between the two anchors — so the ExperienceAndLeveling / RunDirector
// tests can use flat curves for their scaling assertions and trust the ramp is verified here.
public class RunRulesTests
{
    [Fact]
    public void Default_IsAGen1FaithfulNoOp_AtEveryLevel()
    {
        // The default must never scale XP, so untouched callers (tests, the legacy chain) stay pure Gen 1.
        Assert.Equal(1.0, RunRules.Default.XpMultiplierForLevel(1));
        Assert.Equal(1.0, RunRules.Default.XpMultiplierForLevel(50));
        Assert.Equal(1.0, RunRules.Default.XpMultiplierForLevel(Creature.MaxLevel));
    }

    [Fact]
    public void XpCurve_HitsBothAnchorsAtTheLevelExtremes()
    {
        var rules = new RunRules { XpMultiplierEarly = 1.5, XpMultiplierLate = 4.0 };
        Assert.Equal(1.5, rules.XpMultiplierForLevel(1), 6);
        Assert.Equal(4.0, rules.XpMultiplierForLevel(Creature.MaxLevel), 6);
    }

    [Fact]
    public void XpCurve_IsLinearByLevel_HalfwayIsTheMidpoint()
    {
        var rules = new RunRules { XpMultiplierEarly = 1.5, XpMultiplierLate = 4.0 };
        // Level (1 + 99/2) = 50.5 is the exact midpoint of the [1, 100] ramp → the mean of the two anchors.
        double mid = rules.XpMultiplierForLevel(50) + rules.XpMultiplierForLevel(51);
        Assert.Equal((1.5 + 4.0), mid, 3); // the two straddling levels average to the anchor mean
    }

    [Fact]
    public void XpCurve_IncreasesMonotonically_WhenLateExceedsEarly()
    {
        var rules = new RunRules { XpMultiplierEarly = 1.5, XpMultiplierLate = 4.0 };
        double prev = rules.XpMultiplierForLevel(1);
        for (int level = 2; level <= Creature.MaxLevel; level++)
        {
            double cur = rules.XpMultiplierForLevel(level);
            Assert.True(cur >= prev, $"multiplier must not drop from level {level - 1} to {level}");
            prev = cur;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(150)]
    public void XpCurve_ClampsLevelsOutsideTheSupportedRange(int level)
    {
        var rules = new RunRules { XpMultiplierEarly = 1.5, XpMultiplierLate = 4.0 };
        double m = rules.XpMultiplierForLevel(level);
        // Below level 1 → the early anchor; above the cap → the late anchor. Never extrapolates past either.
        Assert.InRange(m, 1.5, 4.0);
    }
}
