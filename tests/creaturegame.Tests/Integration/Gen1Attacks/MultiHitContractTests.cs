using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Multi-hit moves (Double Slap, Comet Punch, …) strike 2–5 times, report the count, and stop on
/// faint. Also pins the Gen 1 distribution itself (2–3 hits dominate 4–5).
/// </summary>
[Collection(MovesCollection.Name)]
public class MultiHitContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("double-slap")] [InlineData("comet-punch")]
    public async Task MultiHitStrikesTwoToFiveTimes(string moveName)
    {
        for (int seed = 0; seed < 25; seed++)
        {
            var result = await new MoveScenario()
                .Defender(TestCreatures.Make("D", hp: 9999, defense: 250))
                .Rng(new SeededRandomSource(seed))
                .Use(Move(moveName));

            int hits = result.Hits.Count;
            Assert.InRange(hits, 2, 5);
            var summary = result.First<MultiHitCompleted>();
            Assert.NotNull(summary);
            Assert.Equal(hits, summary!.Hits);
        }
    }

    [Theory]
    [InlineData("double-slap")] [InlineData("comet-punch")]
    public async Task MultiHitWithFixedCountStrikesExactlyThatMany(string moveName)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 9999, defense: 250))
            .Rules(new FixedMultiHitRules(3))
            .Use(Move(moveName));

        Assert.Equal(3, result.Hits.Count);
        Assert.Equal(3, result.First<MultiHitCompleted>()!.Hits);
        Assert.Equal(result.TotalDamage, result.Defender.Attributes.MaxHP - result.Defender.Attributes.HP);
    }

    [Fact]
    public void RollMultiHitCountStaysInTwoToFiveAndFavoursLowCounts()
    {
        var counts = new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 0, [5] = 0 };
        for (int seed = 0; seed < 2000; seed++)
        {
            int n = new Gen1BattleRules(new SeededRandomSource(seed)).RollMultiHitCount();
            Assert.InRange(n, 2, 5);
            counts[n]++;
        }
        Assert.True(counts[2] + counts[3] > counts[4] + counts[5], "2–3 hits should dominate 4–5");
    }
}
