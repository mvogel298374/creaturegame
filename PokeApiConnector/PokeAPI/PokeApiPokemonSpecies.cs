using System.Text.Json.Serialization;

namespace PokeApiConnector.PokeAPI;

public class PokeApiPokemonSpecies
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("growth_rate")]
    public GrowthRateResource GrowthRate { get; set; }

    [JsonPropertyName("capture_rate")]
    public int CaptureRate { get; set; }

    [JsonPropertyName("flavor_text_entries")]
    public List<FlavorTextEntry> FlavorTextEntries { get; set; }
}

public class FlavorTextEntry
{
    [JsonPropertyName("flavor_text")]
    public string FlavorText { get; set; }

    [JsonPropertyName("language")]
    public NamedApiResource Language { get; set; }
}

public class GrowthRateResource
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }
}
