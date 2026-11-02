using creaturegame.Combat;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>Which moves are high-crit, and their crit rates over seeded runs of the real formula.</summary>
[Collection(MovesCollection.Name)]
public class CriticalHitContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task HighCritMoveCritsFarMoreOftenThanNormal()
    {
        int karateChop = await CritRuns("karate-chop"); // high-crit ⇒ crits almost always
        int pound = await CritRuns("pound"); // normal crit rate

        Assert.True(
            karateChop > pound,
            $"karate-chop crits ({karateChop}) should exceed pound ({pound})"
        );
        Assert.True(
            karateChop >= 40,
            $"high-crit move should crit most of the time (got {karateChop}/60)"
        );
    }

    [Fact]
    public void OnlyPokeredsFourMovesAreHighCrit()
    {
        using var ctx = new MovesDbContext();
        var highCrit = ctx
            .Moves.AsNoTracking()
            .Where(m => m.IsHighCrit)
            .Select(m => m.Name!)
            .OrderBy(n => n)
            .ToList();

        string[] pokered = ["crabhammer", "karate-chop", "razor-leaf", "slash"];
        Assert.Equal(pokered, highCrit);
    }

    // PokeAPI marks both high-crit; Fly is the normal-rate two-turn baseline.
    [Theory]
    [InlineData("razor-wind")]
    [InlineData("sky-attack")]
    public async Task TwoTurnMoveCritsAtTheNormalRateOnRelease(string moveName)
    {
        var attacker = TestCreatures.Make("A", baseSpeed: 100);
        Assert.Equal(
            Gen1BattleRules.Instance.GetCritChance(attacker, Move("fly")),
            Gen1BattleRules.Instance.GetCritChance(attacker, Move(moveName))
        );

        int crits = await ReleaseCrits(moveName);

        Assert.InRange(crits, 1, 25);
    }

    // Razor Leaf is a single-turn high-crit Special move (Gen 1) — same high crit rate, no charge.
    [Fact]
    public async Task RazorLeafCritsFarMoreOftenThanNormal()
    {
        int razorLeaf = await CritRuns("razor-leaf");
        int pound = await CritRuns("pound");

        Assert.True(
            razorLeaf > pound,
            $"razor-leaf crits ({razorLeaf}) should exceed pound ({pound})"
        );
        Assert.True(
            razorLeaf >= 40,
            $"high-crit move should crit most of the time (got {razorLeaf}/60)"
        );
    }

    // Crabhammer is a single-turn high-crit Water move (Gen 1) — same high crit rate, no charge.
    [Fact]
    public async Task CrabhammerCritsFarMoreOftenThanNormal()
    {
        int crabhammer = await CritRuns("crabhammer");
        int pound = await CritRuns("pound");

        Assert.True(
            crabhammer > pound,
            $"crabhammer crits ({crabhammer}) should exceed pound ({pound})"
        );
        Assert.True(
            crabhammer >= 40,
            $"high-crit move should crit most of the time (got {crabhammer}/60)"
        );
    }

    // Slash is a single-turn high-crit Normal move (Gen 1) — same high crit rate, no charge.
    [Fact]
    public async Task SlashCritsFarMoreOftenThanNormal()
    {
        int slash = await CritRuns("slash");
        int pound = await CritRuns("pound");

        Assert.True(slash > pound, $"slash crits ({slash}) should exceed pound ({pound})");
        Assert.True(slash >= 40, $"high-crit move should crit most of the time (got {slash}/60)");
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
            if (result.Hits.Any(h => h.IsCrit))
                crits++;
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
            if (turns[1].Hits.Any(h => h.IsCrit))
                crits++;
        }
        return crits;
    }
}
