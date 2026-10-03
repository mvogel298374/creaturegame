namespace PokeApiConnector.PokeAPI;

using creaturegame.DB;

/// <summary>Turns a PokeAPI <c>/pokemon/{id}</c> response into a generation's learnset (DATA_IMPORT.md §4.6). Which
/// version group and move-id range belong to the generation comes from <see cref="GenerationImportScope"/>.</summary>
public static class LearnsetMapper
{
    private const string LevelUpMethod = "level-up";
    private const string MachineMethod = "machine";

    /// <summary>Extracts the given generation's learnset as (MoveId, LearnLevel, Method) rows, ordered by
    /// method then level then move id for stable persistence. Throws for a generation with no
    /// <see cref="GenerationImportScope"/>.</summary>
    public static IReadOnlyList<(int MoveId, int LearnLevel, LearnMethod Method)> ExtractLearnset(
        PokeApiPokemon pokemon,
        int generation
    )
    {
        var scope = GenerationImportScope.For(generation);
        var lowestLevelByMove = new Dictionary<int, int>();
        var machineMoves = new HashSet<int>();

        foreach (var entry in pokemon.Moves ?? [])
        {
            int moveId = ParseMoveId(entry.Move?.Url);
            if (moveId <= 0 || moveId > scope.MaxMoveId)
                continue;

            foreach (var detail in entry.VersionGroupDetails ?? [])
            {
                if (detail.VersionGroup?.Name != scope.LearnsetVersionGroup)
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
