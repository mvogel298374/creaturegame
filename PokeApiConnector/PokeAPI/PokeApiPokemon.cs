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

    // Each entry = "these types were in effect up to and including this generation"
    [JsonPropertyName("past_types")]
    public List<PastTypeEntry>? PastTypes { get; set; }

    // Every move the species can learn, across all games/methods. We filter this down
    // to Gen 1 (red-blue) level-up entries in LearnsetMapper — no extra API call needed.
    [JsonPropertyName("moves")]
    public List<PokeApiMoveEntry>? Moves { get; set; }
}

public class PokeApiMoveEntry
{
    [JsonPropertyName("move")]
    public NamedApiResource? Move { get; set; }

    [JsonPropertyName("version_group_details")]
    public List<MoveVersionGroupDetail>? VersionGroupDetails { get; set; }
}

public class MoveVersionGroupDetail
{
    [JsonPropertyName("level_learned_at")]
    public int LevelLearnedAt { get; set; }

    [JsonPropertyName("move_learn_method")]
    public NamedApiResource? MoveLearnMethod { get; set; }

    [JsonPropertyName("version_group")]
    public NamedApiResource? VersionGroup { get; set; }
}

public class PastTypeEntry
{
    [JsonPropertyName("generation")]
    public GenerationResource? Generation { get; set; }

    [JsonPropertyName("types")]
    public List<PokemonTypeSlot>? Types { get; set; }
}

public class GenerationResource
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
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
