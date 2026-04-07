using System.Text.Json.Serialization;

namespace PokeApiConnector.PokeAPI;

public class PokeApiPokemon
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("stats")]
    public List<PokemonStat> Stats { get; set; }

    [JsonPropertyName("types")]
    public List<PokemonTypeSlot> Types { get; set; }
}

public class PokemonStat
{
    [JsonPropertyName("base_stat")]
    public int BaseStat { get; set; }

    [JsonPropertyName("stat")]
    public StatResource Stat { get; set; }
}

public class StatResource
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
}

public class PokemonTypeSlot
{
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("type")]
    public TypeResource Type { get; set; }
}

public class TypeResource
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
}
