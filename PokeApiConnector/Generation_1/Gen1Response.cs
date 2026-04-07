using System.Text.Json.Serialization;
using creaturegame.Attacks;

namespace PokeApiConnector.Generation_1;

public class Gen1Response
{
    [JsonPropertyName("moves")]
    public List<GenerationResource> moves { get; set; }

    [JsonPropertyName("pokemon_species")]
    public List<GenerationResource> pokemon_species { get; set; }

    [JsonPropertyName("types")]
    public List<GenerationResource> types { get; set; }
}