using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Mirror Move re-executes the opponent's last used move (in full, through the real engine). It fails
/// if the foe hasn't used a copyable move yet.
/// </summary>
[Collection(MovesCollection.Name)]
public class MirrorMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task MirrorMoveReExecutesTheFoesLastMove()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.LastMoveUsed = Move("tackle"); // the foe's last move

        var result = await new MoveScenario().Defender(defender).Use(Move("mirror-move"));

        Assert.True(result.Has<DamageDealt>(), "the copied Tackle deals damage to the foe");
        Assert.Contains(result.Events, e => e is MoveUsed m && m.MoveName == "tackle");
    }

    [Fact]
    public async Task MirrorMoveFailsWhenTheFoeHasNotMovedYet()
    {
        var defender = TestCreatures.Make("D", hp: 500); // no LastMoveUsed

        var result = await new MoveScenario().Defender(defender).Use(Move("mirror-move"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }
}
