using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for the <see cref="Gen1EvolutionRules"/> seam — how each Gen 1 trigger is interpreted in
/// this roguelite. Pure: a creature, a context, and in-memory edges. Asserts the gen-variable quirks
/// themselves (the level-37 trade conversion; stones dormant on a level-up but ready on a stone-use),
/// per <c>GENERATION_SEAMS.md §5.0.1</c> — testing only the outcome would let a leak sit green.
/// </summary>
public class Gen1EvolutionRulesTests
{
    private static Creature Species(int speciesId, int level) =>
        new("mon") { SpeciesId = speciesId, Level = level };

    private static PokemonEvolution Level(int from, int to, int threshold) =>
        new()
        {
            FromSpeciesId = from,
            ToSpeciesId = to,
            Trigger = EvolutionTrigger.Level,
            LevelThreshold = threshold,
        };

    private static PokemonEvolution Trade(int from, int to) =>
        new()
        {
            FromSpeciesId = from,
            ToSpeciesId = to,
            Trigger = EvolutionTrigger.Trade,
        };

    private static PokemonEvolution Stone(int from, int to, int stoneItemId) =>
        new()
        {
            FromSpeciesId = from,
            ToSpeciesId = to,
            Trigger = EvolutionTrigger.Stone,
            StoneItemId = stoneItemId,
        };

    private static readonly IEvolutionRules Rules = Gen1EvolutionRules.Instance;

    [Fact]
    public void LevelEdge_FiresAtThreshold_NotBelow()
    {
        var edges = new[] { Level(1, 2, 16) };

        Assert.Null(
            Rules.CheckEvolution(Species(1, 15), new EvolutionContext.LeveledTo(15), edges)
        );

        var result = Rules.CheckEvolution(
            Species(1, 16),
            new EvolutionContext.LeveledTo(16),
            edges
        );
        Assert.NotNull(result);
        Assert.Equal(2, result!.ToSpeciesId);
        Assert.Equal(EvolutionTrigger.Level, result.Trigger);
    }

    [Fact]
    public void TradeEdge_ConvertsToLevel37_NotBefore()
    {
        // Machoke(67) → Machamp(68): trade in canon, but in this roguelite it fires at level 37 — and ONLY 37+.
        var edges = new[] { Trade(67, 68) };

        Assert.Equal(37, Gen1EvolutionRules.TradeEvolutionLevel); // pin the documented constant

        Assert.Null(
            Rules.CheckEvolution(Species(67, 36), new EvolutionContext.LeveledTo(36), edges)
        );

        var result = Rules.CheckEvolution(
            Species(67, 37),
            new EvolutionContext.LeveledTo(37),
            edges
        );
        Assert.NotNull(result);
        Assert.Equal(68, result!.ToSpeciesId);
        Assert.Equal(EvolutionTrigger.Trade, result.Trigger); // result reports the real trigger
    }

    [Fact]
    public void StoneEdge_IsDormantOnLevelUp()
    {
        // No bag yet: a level-up never triggers a stone evolution, however high the level.
        var edges = new[] { Stone(25, 26, 83) };

        Assert.Null(
            Rules.CheckEvolution(Species(25, 100), new EvolutionContext.LeveledTo(100), edges)
        );
    }

    [Fact]
    public void StoneEdge_FiresOnMatchingStoneUse_ButReady()
    {
        // The seam is complete: a StoneUsed with the matching item fires it (for when the bag lands).
        var edges = new[] { Stone(25, 26, 83) };

        Assert.Null(
            Rules.CheckEvolution(Species(25, 5), new EvolutionContext.StoneUsed(99), edges)
        );

        var result = Rules.CheckEvolution(
            Species(25, 5),
            new EvolutionContext.StoneUsed(83),
            edges
        );
        Assert.NotNull(result);
        Assert.Equal(26, result!.ToSpeciesId);
        Assert.Equal(EvolutionTrigger.Stone, result.Trigger);
    }

    [Fact]
    public void BranchingStones_PickTheFormForTheUsedStone()
    {
        // Eevee(133): the used stone selects which form among the three branches.
        var edges = new[]
        {
            Stone(133, 134, 84), // water
            Stone(133, 135, 83), // thunder
            Stone(133, 136, 82), // fire
        };

        var result = Rules.CheckEvolution(
            Species(133, 5),
            new EvolutionContext.StoneUsed(83),
            edges
        );

        Assert.NotNull(result);
        Assert.Equal(135, result!.ToSpeciesId); // Jolteon, the thunder branch
    }

    [Fact]
    public void IgnoresEdgesForOtherSpecies()
    {
        var edges = new[] { Level(1, 2, 16) }; // an edge for species 1
        var other = Species(4, 50); // a different species, well past the threshold

        Assert.Null(Rules.CheckEvolution(other, new EvolutionContext.LeveledTo(50), edges));
    }

    [Fact]
    public void NoEdges_ReturnsNull()
    {
        Assert.Null(Rules.CheckEvolution(Species(1, 50), new EvolutionContext.LeveledTo(50), []));
    }
}
