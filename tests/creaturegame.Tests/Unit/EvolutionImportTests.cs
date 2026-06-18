using creaturegame.Evolution;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for the importer's Gen 1 evolution extraction (<see cref="EvolutionMapper"/>). Pure
/// mapping over a built chain DTO — no network, no database. Asserts the Gen 1 filter and that the
/// trigger is stored <i>faithfully</i> (trade stays Trade; the level-37 conversion is the seam's job).
/// </summary>
public class EvolutionImportTests
{
    private static NamedApiResource SpeciesRef(int id) =>
        new() { Name = $"species-{id}", Url = $"https://pokeapi.co/api/v2/pokemon-species/{id}/" };

    private static NamedApiResource ItemRef(int id, string name) =>
        new() { Name = name, Url = $"https://pokeapi.co/api/v2/item/{id}/" };

    private static ChainLink Link(
        int id,
        List<EvolutionDetail>? details,
        params ChainLink[] next
    ) =>
        new()
        {
            Species = SpeciesRef(id),
            EvolutionDetails = details,
            EvolvesTo = next.ToList(),
        };

    private static List<EvolutionDetail> Level(int level) =>
        [
            new()
            {
                Trigger = new NamedApiResource { Name = "level-up" },
                MinLevel = level,
            },
        ];

    private static List<EvolutionDetail> Stone(int itemId, string name) =>
        [
            new()
            {
                Trigger = new NamedApiResource { Name = "use-item" },
                Item = ItemRef(itemId, name),
            },
        ];

    private static List<EvolutionDetail> Trade() =>
        [new() { Trigger = new NamedApiResource { Name = "trade" } }];

    private static PokeApiEvolutionChain Chain(ChainLink root) => new() { Chain = root };

    [Fact]
    public void ExtractGen1Edges_LevelChain_KeepsThresholds()
    {
        // Bulbasaur(1) → Ivysaur(2) @16 → Venusaur(3) @32
        var chain = Chain(Link(1, null, Link(2, Level(16), Link(3, Level(32)))));

        var edges = EvolutionMapper.ExtractGen1Edges(chain);

        Assert.Equal(2, edges.Count);
        Assert.Contains(new MappedEvolutionEdge(1, 2, EvolutionTrigger.Level, 16, null), edges);
        Assert.Contains(new MappedEvolutionEdge(2, 3, EvolutionTrigger.Level, 32, null), edges);
    }

    [Fact]
    public void ExtractGen1Edges_TradeEvolution_StoredFaithfullyAsTrade()
    {
        // Machop(66) → Machoke(67) @28 → Machamp(68) by trade — the trade edge stays Trade.
        var chain = Chain(Link(66, null, Link(67, Level(28), Link(68, Trade()))));

        var edges = EvolutionMapper.ExtractGen1Edges(chain);

        var trade = Assert.Single(edges, e => e.Trigger == EvolutionTrigger.Trade);
        Assert.Equal(67, trade.FromSpeciesId);
        Assert.Equal(68, trade.ToSpeciesId);
        Assert.Null(trade.LevelThreshold); // NOT pre-converted to a level — that's the seam's job
    }

    [Fact]
    public void ExtractGen1Edges_StoneEvolution_CapturesStoneItemId()
    {
        // Pikachu(25) → Raichu(26) via thunder-stone (item 83)
        var chain = Chain(Link(25, null, Link(26, Stone(83, "thunder-stone"))));

        var edge = Assert.Single(EvolutionMapper.ExtractGen1Edges(chain));

        Assert.Equal(EvolutionTrigger.Stone, edge.Trigger);
        Assert.Equal(83, edge.StoneItemId);
        Assert.Null(edge.LevelThreshold);
    }

    [Fact]
    public void ExtractGen1Edges_BranchingStones_KeepsEveryBranch()
    {
        // Eevee(133) → Vaporeon(134)/Jolteon(135)/Flareon(136) by water/thunder/fire stone.
        var chain = Chain(
            Link(
                133,
                null,
                Link(134, Stone(84, "water-stone")),
                Link(135, Stone(83, "thunder-stone")),
                Link(136, Stone(82, "fire-stone"))
            )
        );

        var edges = EvolutionMapper.ExtractGen1Edges(chain);

        Assert.Equal(3, edges.Count);
        Assert.All(edges, e => Assert.Equal(EvolutionTrigger.Stone, e.Trigger));
        Assert.Equal([134, 135, 136], edges.Select(e => e.ToSpeciesId).ToArray());
    }

    [Fact]
    public void ExtractGen1Edges_RejectsHappinessLevelUp_NotAGen1Evolution()
    {
        // A level-up trigger gated by happiness (Gen 2 shape) must be dropped even when both ids are ≤151.
        var happiness = new List<EvolutionDetail>
        {
            new()
            {
                Trigger = new NamedApiResource { Name = "level-up" },
                MinHappiness = 220,
            },
        };
        var chain = Chain(Link(133, null, Link(150, happiness)));

        Assert.Empty(EvolutionMapper.ExtractGen1Edges(chain));
    }

    [Fact]
    public void ExtractGen1Edges_RejectsHeldItemTrade_Gen2Shape()
    {
        var heldTrade = new List<EvolutionDetail>
        {
            new()
            {
                Trigger = new NamedApiResource { Name = "trade" },
                HeldItem = ItemRef(233, "metal-coat"),
            },
        };
        var chain = Chain(Link(95, null, Link(100, heldTrade)));

        Assert.Empty(EvolutionMapper.ExtractGen1Edges(chain));
    }

    [Fact]
    public void ExtractGen1Edges_OneChild_PicksTheValidGen1Detail_AmongAlternates()
    {
        // PokeAPI can list several detail objects for one parent→child edge. When a Gen 2-shaped detail
        // (happiness level-up) precedes the real Gen 1 one (level-up @20), the valid one must still win
        // — the mapper keeps the first that maps, not the first listed.
        var child = new ChainLink
        {
            Species = SpeciesRef(2),
            EvolutionDetails =
            [
                new()
                {
                    Trigger = new NamedApiResource { Name = "level-up" },
                    MinHappiness = 220,
                },
                new()
                {
                    Trigger = new NamedApiResource { Name = "level-up" },
                    MinLevel = 20,
                },
            ],
            EvolvesTo = [],
        };
        var chain = Chain(Link(1, null, child));

        var edge = Assert.Single(EvolutionMapper.ExtractGen1Edges(chain));
        Assert.Equal(EvolutionTrigger.Level, edge.Trigger);
        Assert.Equal(20, edge.LevelThreshold);
    }

    [Fact]
    public void ExtractGen1Edges_RejectsEvolutionsIntoLaterGenSpecies()
    {
        // Golbat(42) → Crobat(169): Crobat is a Gen 2 species, so this Gen 1 dex import drops the edge.
        var chain = Chain(Link(42, null, Link(169, Level(1))));

        Assert.Empty(EvolutionMapper.ExtractGen1Edges(chain));
    }
}
