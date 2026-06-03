using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// High-crit moves crit far more often than their normal-rate counterparts. Crits run on the real
/// Gen 1 speed-based crit formula over many seeded RNG runs (no rules double forces the result) —
/// so this measures the actual engine crit behaviour, not a stubbed flag.
/// </summary>
[Collection(MovesCollection.Name)]
public class CriticalHitContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task HighCritMoveCritsFarMoreOftenThanNormal()
    {
        int karateChop = await CritRuns("karate-chop");   // high-crit ⇒ crits almost always
        int pound      = await CritRuns("pound");          // normal crit rate

        Assert.True(karateChop > pound, $"karate-chop crits ({karateChop}) should exceed pound ({pound})");
        Assert.True(karateChop >= 40, $"high-crit move should crit most of the time (got {karateChop}/60)");
    }

    // Razor Wind is a high-crit move that is also two-turn, so its crit only happens on the release
    // turn. Fly is a normal-crit two-turn move — the natural baseline to compare against.
    [Fact]
    public async Task RazorWindCritsFarMoreOftenThanFlyOnRelease()
    {
        int razorWind = await ReleaseCrits("razor-wind");
        int fly       = await ReleaseCrits("fly");

        Assert.True(razorWind > fly, $"razor-wind crits ({razorWind}) should exceed fly ({fly})");
        Assert.True(razorWind >= 40, $"high-crit move should crit most of the time (got {razorWind}/60)");
    }

    private async Task<int> CritRuns(string moveName)
    {
        int crits = 0;
        for (int seed = 0; seed < 60; seed++)
        {
            var result = await new MoveScenario()
                .Attacker(TestCreatures.Make("A", baseSpeed: 100))
                .Defender(TestCreatures.Make("D", hp: 9999, defense: 250))
                .Rng(new SeededRandomSource(seed))
                .Use(Move(moveName));
            if (result.Hits.Any(h => h.IsCrit)) crits++;
        }
        return crits;
    }

    private async Task<int> ReleaseCrits(string moveName)
    {
        int crits = 0;
        for (int seed = 0; seed < 60; seed++)
        {
            var turns = await new MoveScenario()
                .Attacker(TestCreatures.Make("A", baseSpeed: 100))
                .Defender(TestCreatures.Make("D", hp: 99999, defense: 250))
                .Rng(new SeededRandomSource(seed))
                .UseRepeated(Move(moveName), 2);
            if (turns[1].Hits.Any(h => h.IsCrit)) crits++;
        }
        return crits;
    }
}
