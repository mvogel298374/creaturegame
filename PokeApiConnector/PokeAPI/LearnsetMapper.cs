namespace PokeApiConnector.PokeAPI;

using creaturegame.DB;

/// <summary>
/// Turns a PokeAPI <c>/pokemon/{id}</c> response into our Gen 1 learnset.
/// <para>
/// This is the single place the Gen 1 decision lives (mirrors
/// <c>PokemonImport.Gen1TypeSlots</c> and <c>GameAvailabilitySeeder</c>): PokeAPI returns
/// every move across every game and learn method, so we keep only entries whose
/// <c>version_group</c> is <c>red-blue</c>, by two learn methods — <c>level-up</c> (tagged
/// <see cref="LearnMethod.LevelUp"/>) and <c>machine</c> (TM/HM, tagged <see cref="LearnMethod.Machine"/>).
/// A move learnable both ways is kept as level-up (it's already in the level-up pool). The comment is the
/// spec — change it here, not in the runtime model.
/// </para>
/// </summary>
public static class LearnsetMapper
{
    private const string Gen1VersionGroup = "red-blue";
    private const string LevelUpMethod = "level-up";
    private const string MachineMethod = "machine";

    // Gen 1 has 165 moves; our moves.db is keyed by the PokeAPI move id (1–165). Guard
    // against a stray later-gen move id sneaking in via an unexpected version-group entry.
    private const int MaxGen1MoveId = 165;

    /// <summary>
    /// Extracts the Gen 1 (red-blue) learnset as (MoveId, LearnLevel, Method) rows: every level-up move (lowest
    /// level kept) plus every TM/HM (machine) move the species can learn. One row per move — a move that is both
    /// level-up and machine is emitted as <see cref="LearnMethod.LevelUp"/> (machine adds nothing to its pool
    /// membership). Machine rows carry <c>LearnLevel = 0</c>. Ordered by method (level-up first), then level,
    /// then move id for stable persistence.
    /// </summary>
    public static IReadOnlyList<(
        int MoveId,
        int LearnLevel,
        LearnMethod Method
    )> ExtractGen1Learnset(PokeApiPokemon pokemon)
    {
        // A move can have several version_group_details; keep the lowest Gen 1 level-up level, and note any
        // move learnable by machine (TM/HM).
        var lowestLevelByMove = new Dictionary<int, int>();
        var machineMoves = new HashSet<int>();

        foreach (var entry in pokemon.Moves ?? [])
        {
            int moveId = ParseMoveId(entry.Move?.Url);
            if (moveId is <= 0 or > MaxGen1MoveId)
                continue;

            foreach (var detail in entry.VersionGroupDetails ?? [])
            {
                if (detail.VersionGroup?.Name != Gen1VersionGroup)
                    continue;

                switch (detail.MoveLearnMethod?.Name)
                {
                    case LevelUpMethod:
                        if (
                            !lowestLevelByMove.TryGetValue(moveId, out var existing)
                            || detail.LevelLearnedAt < existing
                        )
                            lowestLevelByMove[moveId] = detail.LevelLearnedAt;
                        break;
                    case MachineMethod:
                        machineMoves.Add(moveId);
                        break;
                }
            }
        }

        var levelUp = lowestLevelByMove.Select(kv =>
            (MoveId: kv.Key, LearnLevel: kv.Value, Method: LearnMethod.LevelUp)
        );
        // Machine moves the species can't also learn by level-up — those are already covered above.
        var machine = machineMoves
            .Where(id => !lowestLevelByMove.ContainsKey(id))
            .Select(id => (MoveId: id, LearnLevel: 0, Method: LearnMethod.Machine));

        return levelUp
            .Concat(machine)
            .OrderBy(x => x.Method)
            .ThenBy(x => x.LearnLevel)
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
