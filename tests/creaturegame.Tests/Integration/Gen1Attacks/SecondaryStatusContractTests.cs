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
    [InlineData("body-slam", StatusCondition.Paralysis)]
    [InlineData("poison-sting", StatusCondition.Poison)]
    [InlineData("twineedle", StatusCondition.Poison)]   // 2 hits + 20% poison
    public async Task AppliesSecondaryStatusOnHit(string moveName, StatusCondition expected)
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.Equal(expected, result.Defender.Status);
        Assert.Contains(result.Events, e => e is StatusApplied);
    }

    [Theory]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    [InlineData("body-slam")] [InlineData("poison-sting")] [InlineData("twineedle")]
    public async Task NoSecondaryStatusOnMiss(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Use(Move(moveName));

        Assert.Equal(StatusCondition.None, result.Defender.Status);
    }

    [Theory]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    [InlineData("body-slam")] [InlineData("poison-sting")] [InlineData("twineedle")]
    public async Task NoSecondaryStatusWhenTargetAlreadyStatused(string moveName)
    {
        var defender = TestCreatures.Make("Defender", hp: 500);
        defender.Status = StatusCondition.Poison;

        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(defender)
            .Use(Move(moveName));

        Assert.Equal(StatusCondition.Poison, result.Defender.Status);   // not overwritten
    }
}
