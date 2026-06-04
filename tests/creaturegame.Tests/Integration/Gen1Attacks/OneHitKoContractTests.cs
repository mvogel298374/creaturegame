using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// One-hit KO moves (Guillotine, Horn Drill): on hit they deal the target's full remaining HP and
/// fell it; they <b>fail</b> (not merely miss) when the target out-speeds the user (the Gen 1 rule —
/// a Speed comparison, not the level check Gen 2 introduced); and they still miss on a failed
/// accuracy roll. Damage runs on the real <c>DamageCategory.OHKO</c> branch — the default rules
/// double only removes the 1/256 accuracy fluke.
/// </summary>
[Collection(MovesCollection.Name)]
public class OneHitKoContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("guillotine")] [InlineData("horn-drill")]
    public async Task DealsFullHpDamageAndFells(string moveName)
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", level: 50, hp: 500, defense: 250))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>());
        Assert.Equal(500, result.TotalDamage);
        Assert.Equal(0, result.Defender.Attributes.HP);
        Assert.False(result.Defender.IsAlive());
    }

    [Theory]
    [InlineData("guillotine")] [InlineData("horn-drill")]
    public async Task FailsWhenTargetOutspeedsUser(string moveName)
    {
        // Gen 1: the OHKO fails outright when the target is faster than the user — a Speed
        // comparison, regardless of level (a higher-level user can't OHKO a faster target).
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50, speed: 50))
            .Defender(TestCreatures.Make("D", level: 50, hp: 500, speed: 200))
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }

    [Theory]
    [InlineData("guillotine")] [InlineData("horn-drill")]
    public async Task SucceedsWhenUserOutspeedsTargetEvenAtLowerLevel(string moveName)
    {
        // The Gen 1 check is Speed only: a lower-level but faster user still lands the OHKO.
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 40, speed: 200))
            .Defender(TestCreatures.Make("D", level: 60, hp: 500, speed: 50))
            .Use(Move(moveName));

        Assert.Equal(500, result.TotalDamage);
        Assert.False(result.Defender.IsAlive());
    }

    [Theory]
    [InlineData("guillotine")] [InlineData("horn-drill")]
    public async Task MissesOnAccuracyFail(string moveName)
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", level: 50, hp: 500))
            .Rules(NeverHitRules.Instance)
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }
}
