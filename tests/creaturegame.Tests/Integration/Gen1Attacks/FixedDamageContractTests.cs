using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Fixed-damage moves deal an exact amount of HP, independent of the attacker/defender stats and the
/// type-effectiveness <i>scaling</i> (no ×2 / ×0.5). Sonic Boom always deals 20 — but a target whose
/// type is outright <b>immune</b> to the move's type (Ghost vs Normal) still takes nothing, a Gen 1
/// rule. (Dragon Rage = 40 joins in its batch.) The move still misses on a failed accuracy roll and
/// spends a PP like any move.
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

    // Fixed damage ignores effectiveness scaling — a resistant (Rock) or neutral (Water) type still
    // takes the full 20, with no ×0.5 / ×2.
    [Theory]
    [InlineData(DamageType.Water)]
    [InlineData(DamageType.Rock)]
    public async Task SonicBoomIgnoresEffectivenessScaling(DamageType defenderType)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: defenderType, hp: 500))
            .Use(Move("sonic-boom"));

        Assert.Equal(20, result.TotalDamage);
    }

    // …but a full type immunity still applies: Sonic Boom is Normal, so a Ghost takes nothing.
    [Fact]
    public async Task SonicBoomDoesNotAffectGhostTypes()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500))
            .Use(Move("sonic-boom"));

        Assert.False(result.Has<DamageDealt>());
        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(255)]
    public async Task DragonRageDealsExactly40RegardlessOfDefenderBulk(int defense)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500, defense: defense, special: defense))
            .Use(Move("dragon-rage"));

        Assert.Equal(40, result.TotalDamage);
        Assert.Equal(40, result.First<DamageDealt>()!.Damage);
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
