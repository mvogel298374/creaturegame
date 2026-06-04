using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Pure status moves afflict the target without dealing damage. Sing inflicts Sleep (a major
/// <see cref="StatusCondition"/>); Supersonic inflicts confusion (a separate per-battle counter).
/// Neither deals damage, and neither effect lands when the move misses.
/// </summary>
[Collection(MovesCollection.Name)]
public class StatusMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task SingPutsTheTargetToSleepWithoutDamage()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("sing"));

        Assert.False(result.Has<DamageDealt>(), "Sing is a status move — no damage");
        Assert.Equal(StatusCondition.Sleep, result.Defender.Status);
        Assert.True(result.Defender.SleepTurns > 0);
        Assert.Contains(result.Events, e => e is StatusApplied s && s.Status == StatusCondition.Sleep);
    }

    [Fact]
    public async Task SingAppliesNoSleepWhenItMisses()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("sing"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(StatusCondition.None, result.Defender.Status);
    }

    [Fact]
    public async Task SupersonicConfusesTheTargetWithoutDamage()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("supersonic"));

        Assert.False(result.Has<DamageDealt>(), "Supersonic is a status move — no damage");
        Assert.True(result.Defender.ConfusedTurns > 0);
        Assert.Contains(result.Events, e => e is ConfusionStarted);
    }

    [Fact]
    public async Task SupersonicDoesNotConfuseOnMiss()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("supersonic"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, result.Defender.ConfusedTurns);
    }
}
