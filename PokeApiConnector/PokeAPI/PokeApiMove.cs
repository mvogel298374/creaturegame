using System.Text.Json.Serialization;

namespace PokeApiConnector.PokeAPI;

public class PokeApiMove
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("accuracy")]
    public int? Accuracy { get; set; }

    [JsonPropertyName("pp")]
    public int? Pp { get; set; }

    [JsonPropertyName("power")]
    public int? Power { get; set; }

    [JsonPropertyName("damage_class")]
    public NamedApiResource? DamageClass { get; set; }

    [JsonPropertyName("type")]
    public NamedApiResource? Type { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("effect_chance")]
    public int? EffectChance { get; set; }

    [JsonPropertyName("effect_entries")]
    public List<EffectEntry>? EffectEntries { get; set; }
}

public class NamedApiResource
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class EffectEntry
{
    [JsonPropertyName("effect")]
    public string? Effect { get; set; }

    [JsonPropertyName("short_effect")]
    public string? ShortEffect { get; set; }

    [JsonPropertyName("language")]
    public NamedApiResource? Language { get; set; }
}
