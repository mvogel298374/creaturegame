using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Moves with a chance-based secondary status (the elemental punches) apply it on hit, never on a
/// miss, and never overwrite an existing major status (Gen 1 single-status rule).
/// </summary>
[Collection(MovesCollection.Name)]
public class SecondaryStatusContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("fire-punch", StatusCondition.Burn)]
    [InlineData("ice-punch", StatusCondition.Freeze)]
    [InlineData("thunder-punch", StatusCondition.Paralysis)]
    [InlineData("poison-sting", StatusCondition.Poison)]
    [InlineData("twineedle", StatusCondition.Poison)] // 2 hits + 20% poison
    [InlineData("ember", StatusCondition.Burn)]
    [InlineData("flamethrower", StatusCondition.Burn)]
    [InlineData("ice-beam", StatusCondition.Freeze)]
    [InlineData("blizzard", StatusCondition.Freeze)]
    [InlineData("thunder-shock", StatusCondition.Paralysis)]
    [InlineData("thunderbolt", StatusCondition.Paralysis)]
    [InlineData("thunder", StatusCondition.Paralysis)]
    [InlineData("smog", StatusCondition.Poison)]
    [InlineData("sludge", StatusCondition.Poison)]
    [InlineData("fire-blast", StatusCondition.Burn)]
    public async Task AppliesSecondaryStatusOnHit(string moveName, StatusCondition expected)
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.Equal(expected, result.Defender.Battle.Status);
        Assert.Contains(result.Events, e => e is StatusApplied);
    }

    [Theory]
    [InlineData("fire-punch")]
    [InlineData("ice-punch")]
    [InlineData("thunder-punch")]
    [InlineData("poison-sting")]
    [InlineData("twineedle")]
    [InlineData("ember")]
    [InlineData("flamethrower")]
    [InlineData("ice-beam")]
    [InlineData("blizzard")]
    [InlineData("thunder-shock")]
    [InlineData("thunderbolt")]
    [InlineData("thunder")]
    [InlineData("smog")]
    [InlineData("sludge")]
    [InlineData("fire-blast")]
    public async Task NoSecondaryStatusOnMiss(string moveName)
    {
        var result = await new MoveScenario().Rules(NeverHitRules.Instance).Use(Move(moveName));

        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
    }

    [Theory]
    [InlineData("fire-punch")]
    [InlineData("ice-punch")]
    [InlineData("thunder-punch")]
    [InlineData("poison-sting")]
    [InlineData("twineedle")]
    [InlineData("ember")]
    [InlineData("flamethrower")]
    [InlineData("ice-beam")]
    [InlineData("blizzard")]
    [InlineData("thunder-shock")]
    [InlineData("thunderbolt")]
    [InlineData("thunder")]
    [InlineData("smog")]
    [InlineData("sludge")]
    [InlineData("fire-blast")]
    public async Task NoSecondaryStatusWhenTargetAlreadyStatused(string moveName)
    {
        var defender = TestCreatures.Make("Defender", hp: 500);
        defender.Battle.Status = StatusCondition.Poison;

        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(defender)
            .Use(Move(moveName));

        Assert.Equal(StatusCondition.Poison, result.Defender.Battle.Status); // not overwritten
    }

    // Body Slam is special-cased: in Gen 1 it can't paralyze Normal-types (see ImmunityContractTests),
    // but it paralyzes a non-Normal target normally.
    [Fact]
    public async Task BodySlamParalyzesANonNormalTarget()
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", type1: DamageType.Water, hp: 500))
            .Use(Move("body-slam"));

        Assert.Equal(StatusCondition.Paralysis, result.Defender.Battle.Status);
        Assert.Contains(result.Events, e => e is StatusApplied);
    }

    // Lick is Ghost-type (0× vs Normal and — the Gen 1 bug — vs Psychic; see ImmunityContractTests).
    // Against a non-immune target it deals damage, spends a PP, and lands its 30% paralysis secondary.
    [Fact]
    public async Task LickDamagesAndParalyzesANonImmuneTarget()
    {
        var move = Move("lick");
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", type1: DamageType.Water, hp: 500))
            .Use(move);

        Assert.True(result.Has<DamageDealt>());
        Assert.True(result.TotalDamage > 0);
        Assert.Equal(StatusCondition.Paralysis, result.Defender.Battle.Status);
        Assert.Equal(move.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }
}
