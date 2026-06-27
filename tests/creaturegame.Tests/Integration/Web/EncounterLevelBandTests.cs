using creaturegame.Combat;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The roguelite wild-encounter difficulty band: <see cref="EncounterFactory.ScaleWildLevel"/> picks a wild
/// foe's level within a window of the player's current level that <b>climbs with run depth</b> — [50%, 80%] at
/// depth 0 (foes a step under the player), lifting toward and past parity as the run goes deeper.
/// <see cref="EncounterFactory.ScaleTargetBst"/> raises the target BST by a flat amount per depth. Both are
/// run-layer tuning, not Gen 1 mechanics. Verifies the depth-0 band, the depth lift, and the BST curve.
/// </summary>
public class EncounterLevelBandTests
{
    [Theory]
    [InlineData(5, 2, 4)] // floor(2.5)=2 (clamped to 2 anyway), floor(4.0)=4
    [InlineData(10, 5, 8)]
    [InlineData(20, 10, 16)]
    [InlineData(50, 25, 40)]
    [InlineData(100, 50, 80)]
    public void ScaleWildLevel_AtDepthZero_StaysWithinFiftyToEightyPercentBand(
        int playerLevel,
        int expectedMin,
        int expectedMax
    )
    {
        var rng = new SeededRandomSource(12345);
        for (int i = 0; i < 500; i++)
        {
            int level = EncounterFactory.ScaleWildLevel(playerLevel, depth: 0, rng);
            Assert.InRange(level, expectedMin, expectedMax);
        }
    }

    [Fact]
    public void ScaleWildLevel_AtDepthZero_ReachesBothEndsOfTheBand_OverManyRolls()
    {
        var rng = new SeededRandomSource(99);
        var seen = new HashSet<int>();
        for (int i = 0; i < 1000; i++)
            seen.Add(EncounterFactory.ScaleWildLevel(50, depth: 0, rng));

        Assert.Contains(25, seen); // 50% of 50
        Assert.Contains(40, seen); // 80% of 50
    }

    [Fact]
    public void ScaleWildLevel_NeverDropsBelowTwo_AtVeryLowPlayerLevels()
    {
        var rng = new SeededRandomSource(7);
        for (int level = 1; level <= 3; level++)
        for (int i = 0; i < 50; i++)
            Assert.True(EncounterFactory.ScaleWildLevel(level, depth: 0, rng) >= 2);
    }

    [Fact]
    public void ScaleWildLevel_DepthLiftsTheBand_AboveTheDepthZeroCeiling()
    {
        // At a deep run the lift caps at +0.40, so the band for L50 is [90%, 120%] = [45, 60] — entirely above
        // the depth-0 ceiling of 40. Proves depth genuinely raises enemy levels past the early-game band.
        var rng = new SeededRandomSource(2024);
        for (int i = 0; i < 500; i++)
        {
            int level = EncounterFactory.ScaleWildLevel(50, depth: 100, rng);
            Assert.InRange(level, 45, 60);
        }
    }

    [Fact]
    public void ScaleTargetBst_AddsAFlatAmountPerDepth()
    {
        Assert.Equal(300, EncounterFactory.ScaleTargetBst(300, depth: 0)); // depth 0 = the player's BST
        Assert.Equal(350, EncounterFactory.ScaleTargetBst(300, depth: 5)); // +10 per depth
        Assert.Equal(400, EncounterFactory.ScaleTargetBst(300, depth: 10));
    }
}
