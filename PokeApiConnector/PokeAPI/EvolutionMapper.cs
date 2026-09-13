using creaturegame.Evolution;

namespace PokeApiConnector.PokeAPI;

/// <summary>One mapped Gen 1 evolution edge, ready to become a <c>PokemonEvolution</c> row.</summary>
public readonly record struct MappedEvolutionEdge(
    int FromSpeciesId,
    int ToSpeciesId,
    EvolutionTrigger Trigger,
    int? LevelThreshold,
    int? StoneItemId
);

/// <summary>Turns a PokeAPI <c>/evolution-chain</c> tree into our Gen 1 evolution edges — the Gen 1
/// filter itself is DATA_IMPORT.md §4.7.</summary>
public static class EvolutionMapper
{
    private const int MaxGen1SpeciesId = 151;

    private const string LevelUpTrigger = "level-up";
    private const string UseItemTrigger = "use-item";
    private const string TradeTrigger = "trade";

    private static readonly HashSet<string> Gen1Stones =
    [
        "fire-stone",
        "water-stone",
        "thunder-stone",
        "leaf-stone",
        "moon-stone",
    ];

    /// <summary>Flattens the chain tree into the Gen 1 edges it contains, ordered by (from, to) for stable persistence.</summary>
    public static IReadOnlyList<MappedEvolutionEdge> ExtractGen1Edges(PokeApiEvolutionChain chain)
    {
        var edges = new List<MappedEvolutionEdge>();
        if (chain.Chain != null)
            Walk(chain.Chain, edges);

        return edges.OrderBy(e => e.FromSpeciesId).ThenBy(e => e.ToSpeciesId).ToList();
    }

    private static void Walk(ChainLink node, List<MappedEvolutionEdge> edges)
    {
        int fromId = ParseTrailingId(node.Species?.Url);

        foreach (var child in node.EvolvesTo ?? [])
        {
            int toId = ParseTrailingId(child.Species?.Url);
            if (IsGen1Species(fromId) && IsGen1Species(toId))
            {
                foreach (var detail in child.EvolutionDetails ?? [])
                {
                    if (TryMap(fromId, toId, detail, out var edge))
                    {
                        edges.Add(edge);
                        break;
                    }
                }
            }

            Walk(child, edges);
        }
    }

    private static bool TryMap(
        int fromId,
        int toId,
        EvolutionDetail detail,
        out MappedEvolutionEdge edge
    )
    {
        edge = default;
        switch (detail.Trigger?.Name)
        {
            case LevelUpTrigger:
                if (
                    detail.MinLevel is not int level
                    || detail.MinHappiness != null
                    || !string.IsNullOrEmpty(detail.TimeOfDay)
                    || detail.HeldItem != null
                )
                    return false;
                edge = new MappedEvolutionEdge(fromId, toId, EvolutionTrigger.Level, level, null);
                return true;

            case UseItemTrigger:
                if (detail.Item?.Name is not string stone || !Gen1Stones.Contains(stone))
                    return false;
                edge = new MappedEvolutionEdge(
                    fromId,
                    toId,
                    EvolutionTrigger.Stone,
                    null,
                    ParseTrailingId(detail.Item.Url)
                );
                return true;

            case TradeTrigger:
                if (detail.HeldItem != null)
                    return false;
                edge = new MappedEvolutionEdge(fromId, toId, EvolutionTrigger.Trade, null, null);
                return true;

            default:
                return false;
        }
    }

    private static bool IsGen1Species(int id) => id is > 0 and <= MaxGen1SpeciesId;

    // PokeAPI resource URLs look like "https://pokeapi.co/api/v2/pokemon-species/3/" — pull the id.
    private static int ParseTrailingId(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return 0;
        var segments = url.TrimEnd('/').Split('/');
        return int.TryParse(segments[^1], out var id) ? id : 0;
    }
}
