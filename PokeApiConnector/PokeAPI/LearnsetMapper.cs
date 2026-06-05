namespace PokeApiConnector.PokeAPI;

/// <summary>
/// Turns a PokeAPI <c>/pokemon/{id}</c> response into our Gen 1 level-up learnset.
/// <para>
/// This is the single place the Gen 1 decision lives (mirrors
/// <c>PokemonImport.Gen1TypeSlots</c> and <c>GameAvailabilitySeeder</c>): PokeAPI returns
/// every move across every game and learn method, so we keep only entries whose
/// <c>version_group</c> is <c>red-blue</c> and whose <c>move_learn_method</c> is
/// <c>level-up</c>. The comment is the spec — change it here, not in the runtime model.
/// </para>
/// </summary>
public static class LearnsetMapper
{
    private const string Gen1VersionGroup = "red-blue";
    private const string LevelUpMethod = "level-up";

    // Gen 1 has 165 moves; our moves.db is keyed by the PokeAPI move id (1–165). Guard
    // against a stray later-gen move id sneaking in via an unexpected version-group entry.
    private const int MaxGen1MoveId = 165;

    /// <summary>
    /// Extracts the Gen 1 (red-blue) level-up learnset as (MoveId, LearnLevel) pairs.
    /// If a move appears more than once, the lowest level is kept (the level it is first
    /// learned at). Result is ordered by level then move id for stable persistence.
    /// </summary>
    public static IReadOnlyList<(int MoveId, int LearnLevel)> ExtractGen1Learnset(
        PokeApiPokemon pokemon
    )
    {
        // A move can have several version_group_details; keep the lowest Gen 1 level-up level.
        var lowestLevelByMove = new Dictionary<int, int>();

        foreach (var entry in pokemon.Moves ?? [])
        {
            int moveId = ParseMoveId(entry.Move?.Url);
            if (moveId is <= 0 or > MaxGen1MoveId)
                continue;

            foreach (var detail in entry.VersionGroupDetails ?? [])
            {
                if (detail.VersionGroup?.Name != Gen1VersionGroup)
                    continue;
                if (detail.MoveLearnMethod?.Name != LevelUpMethod)
                    continue;

                if (
                    !lowestLevelByMove.TryGetValue(moveId, out var existing)
                    || detail.LevelLearnedAt < existing
                )
                    lowestLevelByMove[moveId] = detail.LevelLearnedAt;
            }
        }

        return lowestLevelByMove
            .Select(kv => (MoveId: kv.Key, LearnLevel: kv.Value))
            .OrderBy(x => x.LearnLevel)
            .ThenBy(x => x.MoveId)
            .ToList();
    }

    // PokeAPI move URLs look like "https://pokeapi.co/api/v2/move/22/" — pull the id.
    private static int ParseMoveId(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return 0;
        var segments = url.TrimEnd('/').Split('/');
        return int.TryParse(segments[^1], out var id) ? id : 0;
    }
}
