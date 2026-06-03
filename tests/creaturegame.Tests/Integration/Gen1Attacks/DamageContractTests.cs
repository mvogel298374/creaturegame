using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// The baseline contract every standard damaging move shares: it deals damage on hit, spends one
/// PP, and can miss when its accuracy roll fails. Moves with their own damage formula (OHKO,
/// two-turn) are covered by their dedicated capability classes instead.
/// </summary>
[Collection(MovesCollection.Name)]
public class DamageContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("pound")] [InlineData("karate-chop")] [InlineData("double-slap")]
    [InlineData("comet-punch")] [InlineData("mega-punch")] [InlineData("pay-day")]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    [InlineData("scratch")]
    [InlineData("vice-grip")] [InlineData("cut")] [InlineData("gust")]
    [InlineData("wing-attack")] [InlineData("bind")]
    [InlineData("slam")] [InlineData("vine-whip")] [InlineData("stomp")]
    [InlineData("double-kick")] [InlineData("mega-kick")] [InlineData("jump-kick")]
    [InlineData("rolling-kick")] [InlineData("headbutt")] [InlineData("horn-attack")]
    public async Task DealsDamageOnHit(string moveName)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("Defender", hp: 500, defense: 80, special: 80))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>(), "expected a DamageDealt event");
        Assert.True(result.TotalDamage > 0, "expected non-zero damage");
        Assert.True(result.Defender.Attributes.HP < result.Defender.Attributes.MaxHP);
    }

    [Theory]
    [InlineData("pound")] [InlineData("karate-chop")] [InlineData("double-slap")]
    [InlineData("comet-punch")] [InlineData("mega-punch")] [InlineData("pay-day")]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    [InlineData("scratch")]
    [InlineData("vice-grip")] [InlineData("cut")] [InlineData("gust")]
    [InlineData("wing-attack")] [InlineData("bind")]
    [InlineData("slam")] [InlineData("vine-whip")] [InlineData("stomp")]
    [InlineData("double-kick")] [InlineData("mega-kick")] [InlineData("jump-kick")]
    [InlineData("rolling-kick")] [InlineData("headbutt")] [InlineData("horn-attack")]
    public async Task DecrementsPpByOneOnUse(string moveName)
    {
        var move = Move(moveName);
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("Defender", hp: 9999, defense: 250))
            .Use(move);

        Assert.Equal(move.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }

    // Only the sub-100%-accuracy single-action damaging moves can actually miss.
    // (jump-kick also misses but takes crash damage — covered by CrashDamageContractTests.)
    [Theory]
    [InlineData("double-slap")] [InlineData("comet-punch")] [InlineData("mega-punch")]
    [InlineData("cut")] [InlineData("bind")]
    [InlineData("slam")] [InlineData("mega-kick")] [InlineData("rolling-kick")]
    public async Task MissesWhenAccuracyRollFails(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(result.Defender.Attributes.MaxHP, result.Defender.Attributes.HP);
    }
}
