using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class TypeChartTests
{ // --- Type Chart Tests ---
    [Fact]
    public void Gen1TypeChart_SuperEffective_Returns2x()
    {
        var chart = new Gen1TypeChart();
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Fire, DamageType.Grass));
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Water, DamageType.Fire));
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Electric, DamageType.Water));
    }

    [Fact]
    public void Gen1TypeChart_NotVeryEffective_Returns0Point5x()
    {
        var chart = new Gen1TypeChart();
        Assert.Equal(0.5, chart.GetMultiplier(DamageType.Fire, DamageType.Water));
        Assert.Equal(0.5, chart.GetMultiplier(DamageType.Normal, DamageType.Rock));
        Assert.Equal(0.5, chart.GetMultiplier(DamageType.Grass, DamageType.Fire));
    }

    [Fact]
    public void Gen1TypeChart_Immune_Returns0x()
    {
        var chart = new Gen1TypeChart();
        Assert.Equal(0.0, chart.GetMultiplier(DamageType.Normal, DamageType.Ghost));
        Assert.Equal(0.0, chart.GetMultiplier(DamageType.Electric, DamageType.Ground));
        Assert.Equal(0.0, chart.GetMultiplier(DamageType.Ground, DamageType.Flying));
    }

    [Fact]
    public void Gen1TypeChart_GhostVsPsychic_IsImmune_Gen1Bug()
    {
        // In Gen 1 RBY, Ghost → Psychic = 0x (famous bug; should be 2x)
        var chart = new Gen1TypeChart();
        Assert.Equal(0.0, chart.GetMultiplier(DamageType.Ghost, DamageType.Psychic));
    }

    [Fact]
    public void Gen1TypeChart_PoisonVsBug_Is2x_Gen1Quirk()
    {
        // Changed to 0.5x in Gen 2+
        var chart = new Gen1TypeChart();
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Poison, DamageType.Bug));
    }

    [Fact]
    public void Gen1TypeChart_NeutralMatchup_Returns1x()
    {
        var chart = new Gen1TypeChart();
        Assert.Equal(1.0, chart.GetMultiplier(DamageType.Normal, DamageType.Normal));
        Assert.Equal(1.0, chart.GetMultiplier(DamageType.Fire, DamageType.Normal));
        Assert.Equal(1.0, chart.GetMultiplier(DamageType.Water, DamageType.Electric));
    }

    [Fact]
    public void Gen1TypeChart_DualType_MultipliesCorrectly()
    {
        // Water move vs Grass/Poison (Bulbasaur): 0.5 * 1.0 = 0.5
        var chart = new Gen1TypeChart();
        double effectiveness = DamageCalculator.GetTypeEffectiveness(
            DamageType.Water,
            DamageType.Grass,
            DamageType.Poison,
            chart
        );
        Assert.Equal(0.5, effectiveness);
    }

    [Fact]
    public void Gen1TypeChart_IceVsFire_IsNeutral_Gen1Quirk()
    {
        // Gen 2+: Ice → Fire = 0.5x. Gen 1: 1x (quirk).
        var chart = new Gen1TypeChart();
        Assert.Equal(1.0, chart.GetMultiplier(DamageType.Ice, DamageType.Fire));
    }

    [Fact]
    public void Gen1TypeChart_BugVsPoison_Is2x_Gen1Quirk()
    {
        // Changed to 1x in Gen 2+.
        var chart = new Gen1TypeChart();
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Bug, DamageType.Poison));
    }

    [Fact]
    public void Gen1TypeChart_BugVsPsychic_Is2x_Gen1Quirk()
    {
        // Changed to 1x in Gen 2+.
        var chart = new Gen1TypeChart();
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Bug, DamageType.Psychic));
    }
}
