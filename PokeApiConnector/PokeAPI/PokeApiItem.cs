using System.Text.Json.Serialization;

namespace PokeApiConnector.PokeAPI;

// Mirrors the shape of GET /item/{id}. NamedApiResource and EffectEntry are reused from PokeApiMove.cs
// (same namespace).
public class PokeApiItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("cost")]
    public int Cost { get; set; }

    [JsonPropertyName("fling_power")]
    public int? FlingPower { get; set; }

    [JsonPropertyName("category")]
    public NamedApiResource? Category { get; set; }

    [JsonPropertyName("effect_entries")]
    public List<EffectEntry>? EffectEntries { get; set; }

    [JsonPropertyName("sprites")]
    public ItemSprites? Sprites { get; set; }
}

public class ItemSprites
{
    [JsonPropertyName("default")]
    public string? Default { get; set; }
}
