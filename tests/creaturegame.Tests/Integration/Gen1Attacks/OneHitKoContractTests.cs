using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// One-hit KO moves (Guillotine, …): on hit they deal the target's full remaining HP and fell it;
/// they <b>fail</b> (not merely miss) when the user's level is below the target's (the Gen 1 rule);
/// and they still miss on a failed accuracy roll. Damage runs on the real <c>DamageCategory.OHKO</c>
/// branch — the default rules double only removes the 1/256 accuracy fluke.
/// </summary>
[Collection(MovesCollection.Name)]
public class OneHitKoContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task DealsFullHpDamageAndFells()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", level: 50, hp: 500, defense: 250))
            .Use(Move("guillotine"));

        Assert.True(result.Has<DamageDealt>());
        Assert.Equal(500, result.TotalDamage);
        Assert.Equal(0, result.Defender.Attributes.HP);
        Assert.False(result.Defender.IsAlive());
    }

    [Fact]
    public async Task FailsWhenUserLevelBelowTarget()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", level: 60, hp: 500))
            .Use(Move("guillotine"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }

    [Fact]
    public async Task MissesOnAccuracyFail()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", level: 50, hp: 500))
            .Rules(NeverHitRules.Instance)
            .Use(Move("guillotine"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }
}
