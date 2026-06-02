using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for the importer's Gen 1 learnset extraction. Pure mapping over a built
/// DTO fixture — no network, no database.
/// </summary>
public class LearnsetImportTests
{
    // Builds a move entry the way PokeAPI shapes it: a move URL + per-version-group details.
    private static PokeApiMoveEntry MoveEntry(int moveId, params (int level, string method, string versionGroup)[] details) =>
        new()
        {
            Move = new NamedApiResource { Name = $"move-{moveId}", Url = $"https://pokeapi.co/api/v2/move/{moveId}/" },
            VersionGroupDetails = details.Select(d => new MoveVersionGroupDetail
            {
                LevelLearnedAt  = d.level,
                MoveLearnMethod = new NamedApiResource { Name = d.method },
                VersionGroup    = new NamedApiResource { Name = d.versionGroup },
            }).ToList(),
        };

    [Fact]
    public void ExtractGen1Learnset_KeepsOnlyRedBlueLevelUpEntries()
    {
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves =
            [
                MoveEntry(33, (1,  "level-up", "red-blue")),   // ✓ Tackle
                MoveEntry(22, (13, "level-up", "red-blue")),   // ✓ Vine Whip
                MoveEntry(75, (10, "machine",  "red-blue")),   // ✗ TM, not level-up
                MoveEntry(76, (1,  "level-up", "yellow")),     // ✗ wrong version group
                MoveEntry(14, (20, "level-up", "gold-silver")),// ✗ Gen 2 version group
            ],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal(2, result.Count);
        Assert.Contains((33, 1),  result);
        Assert.Contains((22, 13), result);
    }

    [Fact]
    public void ExtractGen1Learnset_KeepsLowestLevelWhenMoveRepeats()
    {
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves =
            [
                MoveEntry(33, (7, "level-up", "red-blue"), (1, "level-up", "red-blue")),
            ],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal((33, 1), Assert.Single(result));
    }

    [Fact]
    public void ExtractGen1Learnset_SkipsMovesOutsideGen1IdRange()
    {
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves =
            [
                MoveEntry(33,  (1, "level-up", "red-blue")),   // ✓ in range
                MoveEntry(200, (1, "level-up", "red-blue")),   // ✗ id > 165
            ],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal((33, 1), Assert.Single(result));
    }

    [Fact]
    public void ExtractGen1Learnset_OrdersByLevelThenMoveId()
    {
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves =
            [
                MoveEntry(45, (1,  "level-up", "red-blue")),
                MoveEntry(33, (1,  "level-up", "red-blue")),
                MoveEntry(22, (13, "level-up", "red-blue")),
            ],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal([(33, 1), (45, 1), (22, 13)], result);
    }

    [Fact]
    public void ExtractGen1Learnset_HandlesNullMovesArray()
    {
        var result = LearnsetMapper.ExtractGen1Learnset(new PokeApiPokemon { Id = 1, Moves = null });
        Assert.Empty(result);
    }
}
