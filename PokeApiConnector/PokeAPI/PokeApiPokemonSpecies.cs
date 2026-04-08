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
}

public class GrowthRateResource
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }
}
