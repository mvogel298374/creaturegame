using System.Collections;
using System.Text.Json.Serialization;

namespace PokeApiConnector.Generation_1;

public class GenerationResource
{
    [JsonPropertyName("name")]
    public string? name { get; set; }
    
    [JsonPropertyName("url")]
    public string? url { get; set; }
}