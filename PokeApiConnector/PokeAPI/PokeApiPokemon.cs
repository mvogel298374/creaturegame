using System.Text.Json.Serialization;

namespace PokeApiConnector.PokeAPI;

public class PokeApiPokemon
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("stats")]
    public List<PokemonStat>? Stats { get; set; }

    [JsonPropertyName("types")]
    public List<PokemonTypeSlot>? Types { get; set; }

    [JsonPropertyName("base_experience")]
    public int? BaseExperience { get; set; }

    [JsonPropertyName("game_indices")]
    public List<PokemonGameIndex>? GameIndices { get; set; }
}

public class PokemonGameIndex
{
    [JsonPropertyName("game_index")]
    public int GameIndex { get; set; }

    [JsonPropertyName("version")]
    public VersionResource? Version { get; set; }
}

public class VersionResource
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class PokemonStat
{
    [JsonPropertyName("base_stat")]
    public int BaseStat { get; set; }

    [JsonPropertyName("stat")]
    public StatResource? Stat { get; set; }
}

public class StatResource
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class PokemonTypeSlot
{
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("type")]
    public TypeResource? Type { get; set; }
}

public class TypeResource
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
