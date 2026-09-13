namespace PokeApiConnector.PokeAPI;

using creaturegame.DB;

/// <summary>Turns a PokeAPI <c>/pokemon/{id}</c> response into our Gen 1 learnset (DATA_IMPORT.md §4.6).</summary>
public static class LearnsetMapper
{
    private const string Gen1VersionGroup = "red-blue";
    private const string LevelUpMethod = "level-up";
    private const string MachineMethod = "machine";
    private const int MaxGen1MoveId = 165; // guards against a stray later-gen move id

    /// <summary>Extracts the Gen 1 (red-blue) learnset as (MoveId, LearnLevel, Method) rows, ordered by
    /// method then level then move id for stable persistence.</summary>
    public static IReadOnlyList<(
        int MoveId,
        int LearnLevel,
        LearnMethod Method
    )> ExtractGen1Learnset(PokeApiPokemon pokemon)
    {
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
