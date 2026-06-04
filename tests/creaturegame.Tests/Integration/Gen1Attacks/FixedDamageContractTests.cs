using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Fixed-damage moves deal an exact amount of HP, independent of the attacker/defender stats and the
/// type matchup (including immunities). Sonic Boom always deals 20. (Dragon Rage = 40 joins in its
/// batch.) The move still misses on a failed accuracy roll and spends a PP like any move.
/// </summary>
[Collection(MovesCollection.Name)]
public class FixedDamageContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData(40)]
    [InlineData(255)]
    public async Task SonicBoomDealsExactly20RegardlessOfDefenderBulk(int defense)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500, defense: defense, special: defense))
            .Use(Move("sonic-boom"));

        Assert.Equal(20, result.TotalDamage);
        Assert.Equal(20, result.First<DamageDealt>()!.Damage);
    }

    // Fixed damage bypasses type effectiveness entirely — even a Ghost (which Normal can't touch)
    // takes the full 20.
    [Theory]
    [InlineData(DamageType.Water)]
    [InlineData(DamageType.Rock)]
    [InlineData(DamageType.Ghost)]
    public async Task SonicBoomIgnoresTheTypeMatchup(DamageType defenderType)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: defenderType, hp: 500))
            .Use(Move("sonic-boom"));

        Assert.Equal(20, result.TotalDamage);
    }

    [Fact]
    public async Task SonicBoomCanMiss()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("sonic-boom"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task SonicBoomDecrementsPpByOne()
    {
        var move = Move("sonic-boom");
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(move);

        Assert.Equal(move.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }
}
