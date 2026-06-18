using creaturegame.DB;
using creaturegame.Evolution;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Pins the importer's <b>Gen 1 evolution edges</b> in the live <c>pokemon.db</c> — the evolution
/// analogue of <c>SecondaryChanceDataContractTests</c>. PokeAPI's <c>/evolution-chain</c> reports the
/// <i>modern</i> tree (Eevee's 8 forms, friendship/held-item triggers, baby pre-evos), so the Gen 1
/// filter in <see cref="PokeApiConnector.PokeAPI.EvolutionMapper"/> is doing real work; without these
/// pins a re-import or a filter regression could silently change the set and the seam tests (which use
/// hand-built edges) would stay green. Guards the imported rows directly.
/// <para>Requires <c>pokemon.db</c> populated — run <c>PokeApiConnector</c> on a fresh checkout.</para>
/// </summary>
public class PokemonEvolutionDataContractTests
{
    private static List<PokemonEvolution> LoadGen1Edges()
    {
        using var ctx = new PokemonDbContext();
        List<PokemonEvolution> edges;
        try
        {
            edges = ctx.Evolutions.AsNoTracking().Where(e => e.Generation == 1).ToList();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not read PokemonEvolution from pokemon.db. Run "
                    + "`dotnet run --project PokeApiConnector` (or `-- evolutions`) to populate it.",
                ex
            );
        }

        if (edges.Count == 0)
            throw new InvalidOperationException(
                "pokemon.db has no Gen 1 evolution edges. Run `dotnet run --project PokeApiConnector -- evolutions`."
            );

        return edges;
    }

    [Fact]
    public void TriggerBreakdownMatchesGen1()
    {
        var edges = LoadGen1Edges();

        // Gen 1 canon: 52 level evolutions, 16 stone evolutions (3 Fire + 4 Water + 2 Thunder +
        // 3 Leaf + 4 Moon), 4 trade evolutions. Total 72.
        Assert.Equal(72, edges.Count);
        Assert.Equal(52, edges.Count(e => e.Trigger == EvolutionTrigger.Level));
        Assert.Equal(16, edges.Count(e => e.Trigger == EvolutionTrigger.Stone));
        Assert.Equal(4, edges.Count(e => e.Trigger == EvolutionTrigger.Trade));
    }

    [Fact]
    public void TradeEdges_AreTheFourGen1Lines_StoredFaithfully()
    {
        var trade = LoadGen1Edges()
            .Where(e => e.Trigger == EvolutionTrigger.Trade)
            .Select(e => (e.FromSpeciesId, e.ToSpeciesId))
            .ToHashSet();

        // Kadabra→Alakazam, Machoke→Machamp, Graveler→Golem, Haunter→Gengar — and stored as Trade
        // (no level baked in; the level-37 conversion is the seam's job, not the data's).
        Assert.Equal(new HashSet<(int, int)> { (64, 65), (67, 68), (75, 76), (93, 94) }, trade);
        Assert.All(
            LoadGen1Edges().Where(e => e.Trigger == EvolutionTrigger.Trade),
            e =>
            {
                Assert.Null(e.LevelThreshold);
                Assert.Null(e.StoneItemId);
            }
        );
    }

    [Fact]
    public void EeveeBranchesToThreeStoneForms_WithDistinctStones()
    {
        var eevee = LoadGen1Edges().Where(e => e.FromSpeciesId == 133).ToList();

        Assert.Equal(3, eevee.Count);
        Assert.All(eevee, e => Assert.Equal(EvolutionTrigger.Stone, e.Trigger));
        Assert.Equal(
            new HashSet<int> { 134, 135, 136 }, // Vaporeon / Jolteon / Flareon
            eevee.Select(e => e.ToSpeciesId).ToHashSet()
        );
        Assert.Equal(3, eevee.Select(e => e.StoneItemId).Distinct().Count());
    }

    [Fact]
    public void LevelEdges_CarryThresholds_AndKnownChainIsCorrect()
    {
        var edges = LoadGen1Edges();

        Assert.All(
            edges.Where(e => e.Trigger == EvolutionTrigger.Level),
            e => Assert.NotNull(e.LevelThreshold)
        );

        // Bulbasaur(1)→Ivysaur(2)@16→Venusaur(3)@32 — a stable, well-known Gen 1 chain.
        Assert.Equal(
            16,
            edges.Single(e => e.FromSpeciesId == 1 && e.ToSpeciesId == 2).LevelThreshold
        );
        Assert.Equal(
            32,
            edges.Single(e => e.FromSpeciesId == 2 && e.ToSpeciesId == 3).LevelThreshold
        );
        // Pikachu(25)→Raichu(26) is a Thunder Stone evolution, not level.
        Assert.Equal(
            EvolutionTrigger.Stone,
            edges.Single(e => e.FromSpeciesId == 25 && e.ToSpeciesId == 26).Trigger
        );
    }

    [Fact]
    public void EveryEdgeStaysWithinTheGen1Dex()
    {
        var edges = LoadGen1Edges();

        // The dex filter held on both ends — no later-gen species (Crobat 169, Espeon 196, baby
        // pre-evos 172+, etc.) leaked in.
        Assert.All(
            edges,
            e =>
            {
                Assert.InRange(e.FromSpeciesId, 1, 151);
                Assert.InRange(e.ToSpeciesId, 1, 151);
            }
        );
    }
}
