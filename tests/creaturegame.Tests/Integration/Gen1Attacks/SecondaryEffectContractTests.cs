using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Damaging moves whose secondary effect is <i>not</i> a major status (those live in
/// <see cref="SecondaryStatusContractTests"/>): a chance-based stat drop on the foe (Acid lowers
/// Defense in Gen 1) and a chance-based confusion (Psybeam). Each lands on hit when the secondary
/// roll succeeds and never on a miss. The chance is gated through
/// <see cref="IBattleRules.GetSecondaryEffectChance"/>, so <see cref="ForceSecondaryRules"/> forces it.
/// </summary>
[Collection(MovesCollection.Name)]
public class SecondaryEffectContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    // Gen 1's chance-based stat-drop beams: Acid (−1 foe Defense), Bubble Beam (−1 Speed),
    // Aurora Beam (−1 Attack). All deal damage and apply the drop when the secondary roll lands.
    [Theory]
    [InlineData("acid", "Defense")]
    [InlineData("bubble-beam", "Speed")]
    [InlineData("aurora-beam", "Attack")]
    [InlineData("psychic", "Special")] // 10% to lower the foe's (combined) Special in Gen 1
    public async Task LowersTheFoesStatAsASecondaryEffectOnHit(string moveName, string stat)
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>(), $"{moveName} deals damage");
        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Defender.Name, change!.CreatureName);
        Assert.Equal(stat, change.Stat);
        Assert.Equal(-1, change.Delta);
    }

    [Theory]
    [InlineData("acid")]
    [InlineData("bubble-beam")]
    [InlineData("aurora-beam")]
    [InlineData("psychic")]
    public async Task DoesNotLowerAnyStatOnMiss(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<StatStageChanged>());
    }

    // Damaging moves with a chance-based confusion secondary: Psybeam and Confusion (the move).
    [Theory]
    [InlineData("psybeam")]
    [InlineData("confusion")]
    public async Task DamagingMoveCanConfuseAsASecondaryEffect(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>(), $"{moveName} deals damage");
        Assert.True(result.Defender.ConfusedTurns > 0);
        Assert.Contains(result.Events, e => e is ConfusionStarted);
    }

    [Theory]
    [InlineData("psybeam")]
    [InlineData("confusion")]
    public async Task DamagingMoveDoesNotConfuseOnMiss(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, result.Defender.ConfusedTurns);
    }
}
