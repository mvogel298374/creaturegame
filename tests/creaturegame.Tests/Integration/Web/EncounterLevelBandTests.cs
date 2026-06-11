using creaturegame.Combat;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The roguelite wild-encounter difficulty band: <see cref="EncounterFactory.ScaleWildLevel"/> picks a wild
/// foe's level uniformly in [50%, 80%] of the player's current level (floored, never below 2), keeping foes a
/// step under the player so the endless chain stays winnable while still scaling up. Verifies the band's
/// bounds across a range of player levels and that the whole band is actually reachable.
/// </summary>
public class EncounterLevelBandTests
{
    [Theory]
    [InlineData(5, 2, 4)] // floor(2.5)=2 (clamped to 2 anyway), floor(4.0)=4
    [InlineData(10, 5, 8)]
    [InlineData(20, 10, 16)]
    [InlineData(50, 25, 40)]
    [InlineData(100, 50, 80)]
    public void ScaleWildLevel_StaysWithinFiftyToEightyPercentBand(
        int playerLevel,
        int expectedMin,
        int expectedMax
    )
    {
        var rng = new SeededRandomSource(12345);
        for (int i = 0; i < 500; i++)
        {
            int level = EncounterFactory.ScaleWildLevel(playerLevel, rng);
            Assert.InRange(level, expectedMin, expectedMax);
        }
    }

    [Fact]
    public void ScaleWildLevel_ReachesBothEndsOfTheBand_OverManyRolls()
    {
        var rng = new SeededRandomSource(99);
        var seen = new HashSet<int>();
        for (int i = 0; i < 1000; i++)
            seen.Add(EncounterFactory.ScaleWildLevel(50, rng));

        Assert.Contains(25, seen); // 50% of 50
        Assert.Contains(40, seen); // 80% of 50
    }

    [Fact]
    public void ScaleWildLevel_NeverDropsBelowTwo_AtVeryLowPlayerLevels()
    {
        var rng = new SeededRandomSource(7);
        for (int level = 1; level <= 3; level++)
        for (int i = 0; i < 50; i++)
            Assert.True(EncounterFactory.ScaleWildLevel(level, rng) >= 2);
    }
}
