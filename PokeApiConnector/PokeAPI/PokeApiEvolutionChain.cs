using System.Text.Json.Serialization;

namespace PokeApiConnector.PokeAPI;

/// <summary>
/// PokeAPI <c>/evolution-chain/{id}</c> response. The chain is a tree: <see cref="Chain"/> is the
/// base form, each node's <see cref="ChainLink.EvolvesTo"/> lists the next forms, and the
/// <see cref="ChainLink.EvolutionDetails"/> on a node describe how that node is reached from its parent.
/// </summary>
public class PokeApiEvolutionChain
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("chain")]
    public ChainLink? Chain { get; set; }
}

public class ChainLink
{
    [JsonPropertyName("species")]
    public NamedApiResource? Species { get; set; }

    /// <summary>How this node evolves from its parent (empty for the base form).</summary>
    [JsonPropertyName("evolution_details")]
    public List<EvolutionDetail>? EvolutionDetails { get; set; }

    [JsonPropertyName("evolves_to")]
    public List<ChainLink>? EvolvesTo { get; set; }
}

public class EvolutionDetail
{
    [JsonPropertyName("trigger")]
    public NamedApiResource? Trigger { get; set; }

    [JsonPropertyName("min_level")]
    public int? MinLevel { get; set; }

    /// <summary>The stone for a <c>use-item</c> trigger (e.g. <c>thunder-stone</c>); null otherwise.</summary>
    [JsonPropertyName("item")]
    public NamedApiResource? Item { get; set; }

    // Later-generation conditions we use to reject non-Gen-1 evolutions that reuse the level-up
    // trigger (e.g. Eevee → Espeon is "level-up" gated by happiness, not a Gen 1 evolution).
    [JsonPropertyName("min_happiness")]
    public int? MinHappiness { get; set; }

    [JsonPropertyName("time_of_day")]
    public string? TimeOfDay { get; set; }

    [JsonPropertyName("held_item")]
    public NamedApiResource? HeldItem { get; set; }
}
