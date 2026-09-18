using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="EvolutionMinLevel"/> — the lowest level a species could legitimately be
/// encountered at, derived from its evolution chain (<c>ENCOUNTER_DESIGN.md §3.8</c>). Pure: in-memory
/// edges only, no DB. Spot-checks real Gen 1 chains (through <see cref="Gen1EvolutionRules"/>, the seam
/// <see cref="EvolutionMinLevel.Compute"/> consults rather than hardcoding) alongside the trigger-specific
/// rules, so a future re-import or a change to <see cref="Gen1EvolutionRules.MinLevelFor"/> trips a test
/// instead of a live encounter.
/// </summary>
public class EvolutionMinLevelTests
{
    private static readonly IEvolutionRules Gen1Rules = Gen1EvolutionRules.Instance;

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

    [Fact]
    public void BaseForm_WithNoIncomingEdge_FloorsAtZero()
    {
        var edges = new[] { Level(1, 2, 16) }; // Bulbasaur → Ivysaur; Bulbasaur (1) itself has no incoming edge

        Assert.Equal(0, EvolutionMinLevel.Compute(1, edges, Gen1Rules));
    }

    [Fact]
    public void SingleLevelEdge_FloorsAtItsThreshold()
    {
        // Weedle(13) → Kakuna(14) at level 7.
        var edges = new[] { Level(13, 14, 7) };

        Assert.Equal(7, EvolutionMinLevel.Compute(14, edges, Gen1Rules));
    }

    [Fact]
    public void TwoStageLevelChain_FloorsAtTheHigherThreshold_NotTheSum()
    {
        // Weedle(13) →@7 Kakuna(14) →@10 Beedrill(15): reaching Beedrill already implies level 7, so the
        // floor is max(7, 10) = 10, never 7 + 10.
        var edges = new[] { Level(13, 14, 7), Level(14, 15, 10) };

        Assert.Equal(10, EvolutionMinLevel.Compute(15, edges, Gen1Rules));
    }

    [Fact]
    public void ThreeStageLevelChain_CharmanderLine_FloorsAtTheFinalThreshold()
    {
        // Charmander(4) →@16 Charmeleon(5) →@36 Charizard(6).
        var edges = new[] { Level(4, 5, 16), Level(5, 6, 36) };

        Assert.Equal(0, EvolutionMinLevel.Compute(4, edges, Gen1Rules));
        Assert.Equal(16, EvolutionMinLevel.Compute(5, edges, Gen1Rules));
        Assert.Equal(36, EvolutionMinLevel.Compute(6, edges, Gen1Rules));
    }

    [Theory]
    [InlineData(64, 65)] // Kadabra → Alakazam
    [InlineData(67, 68)] // Machoke → Machamp
    [InlineData(75, 76)] // Graveler → Golem
    [InlineData(93, 94)] // Haunter → Gengar
    public void TradeEdge_FloorsAtTheRoguelitesTradeLevel(int from, int to)
    {
        var edges = new[] { Trade(from, to) };

        Assert.Equal(
            Gen1EvolutionRules.TradeEvolutionLevel,
            EvolutionMinLevel.Compute(to, edges, Gen1Rules)
        );
    }

    [Fact]
    public void TradeEdge_StillInheritsAHigherPredecessorFloor()
    {
        // A hypothetical chain where the pre-evolution's own level requirement exceeds the trade floor —
        // Math.Max must keep the higher of the two, not always default to 37.
        var edges = new[] { Level(1, 2, 40), Trade(2, 3) };

        Assert.Equal(40, EvolutionMinLevel.Compute(3, edges, Gen1Rules));
    }

    [Fact]
    public void StoneEdge_AddsNoFloorOfItsOwn()
    {
        // Vulpix(37) → Ninetales(38) via Fire Stone: real Gen 1 places no level requirement on a stone
        // evolution, so a wild Ninetales can legitimately be any level.
        var edges = new[] { Stone(37, 38, 82) };

        Assert.Equal(0, EvolutionMinLevel.Compute(38, edges, Gen1Rules));
    }

    [Fact]
    public void StoneEdge_StillInheritsItsPredecessorsFloor()
    {
        // Poliwag(60) →@25 Poliwhirl(61) → Poliwrath(62) via Water Stone: the stone itself adds nothing, but
        // Poliwrath still can't be below the level it took to become Poliwhirl in the first place.
        var edges = new[] { Level(60, 61, 25), Stone(61, 62, 84) };

        Assert.Equal(25, EvolutionMinLevel.Compute(62, edges, Gen1Rules));
    }

    [Fact]
    public void UnrelatedEdges_AreIgnored()
    {
        var edges = new[] { Level(1, 2, 16), Level(4, 5, 16) }; // an edge for a different species entirely

        Assert.Equal(0, EvolutionMinLevel.Compute(999, edges, Gen1Rules));
    }

    [Fact]
    public void CyclicEdges_EndTheWalk_InsteadOfOverflowingTheStack()
    {
        // A malformed/self-referential edge (never expected from a real import, but the walk must not trust
        // that): 1 → 2 → 1. Compute must terminate rather than recurse/loop forever.
        var edges = new[] { Level(1, 2, 10), Level(2, 1, 5) };

        Assert.Equal(10, EvolutionMinLevel.Compute(2, edges, Gen1Rules));
    }

    private sealed class StubRules : IEvolutionRules
    {
        public EvolutionResult? CheckEvolution(
            Creature creature,
            EvolutionContext context,
            IReadOnlyList<PokemonEvolution> edges
        ) => null;

        public int MinLevelFor(PokemonEvolution edge) =>
            edge.Trigger == EvolutionTrigger.Trade ? 999 : 0; // deliberately unlike Gen1EvolutionRules
    }

    [Fact]
    public void Compute_ConsultsTheInjectedRulesSeam_NotAHardcodedGen1Reference()
    {
        // If Compute ever hardcoded Gen1EvolutionRules internally instead of taking the seam as a parameter,
        // this would still return 37 (TradeEvolutionLevel) instead of the stub's 999.
        var edges = new[] { Trade(1, 2) };

        Assert.Equal(999, EvolutionMinLevel.Compute(2, edges, new StubRules()));
    }
}
