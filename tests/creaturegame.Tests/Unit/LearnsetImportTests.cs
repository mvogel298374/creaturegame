using creaturegame.DB;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for the importer's Gen 1 learnset extraction. Pure mapping over a built
/// DTO fixture — no network, no database.
/// </summary>
public class LearnsetImportTests
{
    // Builds a move entry the way PokeAPI shapes it: a move URL + per-version-group details.
    private static PokeApiMoveEntry MoveEntry(
        int moveId,
        params (int level, string method, string versionGroup)[] details
    ) =>
        new()
        {
            Move = new NamedApiResource
            {
                Name = $"move-{moveId}",
                Url = $"https://pokeapi.co/api/v2/move/{moveId}/",
            },
            VersionGroupDetails = details
                .Select(d => new MoveVersionGroupDetail
                {
                    LevelLearnedAt = d.level,
                    MoveLearnMethod = new NamedApiResource { Name = d.method },
                    VersionGroup = new NamedApiResource { Name = d.versionGroup },
                })
                .ToList(),
        };

    [Fact]
    public void ExtractGen1Learnset_KeepsRedBlueLevelUpAndMachine_ExcludesOtherVersionGroups()
    {
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves =
            [
                MoveEntry(33, (1, "level-up", "red-blue")), // ✓ Tackle (level-up)
                MoveEntry(22, (13, "level-up", "red-blue")), // ✓ Vine Whip (level-up)
                MoveEntry(75, (0, "machine", "red-blue")), // ✓ TM, now kept as Machine
                MoveEntry(76, (1, "level-up", "yellow")), // ✗ wrong version group
                MoveEntry(14, (20, "level-up", "gold-silver")), // ✗ Gen 2 version group
            ],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal(3, result.Count);
        Assert.Contains((33, 1, LearnMethod.LevelUp), result);
        Assert.Contains((22, 13, LearnMethod.LevelUp), result);
        Assert.Contains((75, 0, LearnMethod.Machine), result);
    }

    [Fact]
    public void ExtractGen1Learnset_KeepsLowestLevelWhenMoveRepeats()
    {
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves = [MoveEntry(33, (7, "level-up", "red-blue"), (1, "level-up", "red-blue"))],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal((33, 1, LearnMethod.LevelUp), Assert.Single(result));
    }

    [Fact]
    public void ExtractGen1Learnset_MoveLearnableBothWays_KeptAsLevelUp()
    {
        // A move that is both a level-up move and a TM is already in the level-up pool, so it is emitted once,
        // as LevelUp — never duplicated as a separate Machine row.
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves = [MoveEntry(34, (20, "level-up", "red-blue"), (0, "machine", "red-blue"))],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal((34, 20, LearnMethod.LevelUp), Assert.Single(result));
    }

    [Fact]
    public void ExtractGen1Learnset_SkipsMovesOutsideGen1IdRange()
    {
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves =
            [
                MoveEntry(33, (1, "level-up", "red-blue")), // ✓ in range
                MoveEntry(200, (1, "level-up", "red-blue")), // ✗ id > 165
                MoveEntry(201, (0, "machine", "red-blue")), // ✗ id > 165 (machine too)
            ],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal((33, 1, LearnMethod.LevelUp), Assert.Single(result));
    }

    [Fact]
    public void ExtractGen1Learnset_OrdersLevelUpBeforeMachine_ThenByLevelThenId()
    {
        var pokemon = new PokeApiPokemon
        {
            Id = 1,
            Moves =
            [
                MoveEntry(85, (0, "machine", "red-blue")), // machine — sorts last
                MoveEntry(45, (1, "level-up", "red-blue")),
                MoveEntry(33, (1, "level-up", "red-blue")),
                MoveEntry(22, (13, "level-up", "red-blue")),
            ],
        };

        var result = LearnsetMapper.ExtractGen1Learnset(pokemon);

        Assert.Equal(
            [
                (33, 1, LearnMethod.LevelUp),
                (45, 1, LearnMethod.LevelUp),
                (22, 13, LearnMethod.LevelUp),
                (85, 0, LearnMethod.Machine),
            ],
            result
        );
    }

    [Fact]
    public void ExtractGen1Learnset_HandlesNullMovesArray()
    {
        var result = LearnsetMapper.ExtractGen1Learnset(
            new PokeApiPokemon { Id = 1, Moves = null }
        );
        Assert.Empty(result);
    }
}
