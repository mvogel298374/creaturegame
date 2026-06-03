using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Stat-stage moves change a battler's stat stages and deal no damage. Swords Dance raises the
/// <b>user's</b> Attack by two stages (a self-targeting buff). Foe-targeting drops (Growl, Leer, …)
/// join this class as later batches add them.
/// </summary>
[Collection(MovesCollection.Name)]
public class StatStageMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task SwordsDanceRaisesUserAttackByTwoStages()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Use(Move("swords-dance"));

        Assert.Equal(2, result.Attacker.Stages.Attack);
        Assert.False(result.Has<DamageDealt>(), "Swords Dance is a status move — no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Attacker.Name, change!.CreatureName);   // affects the user, not the foe
        Assert.Equal(2, change.Delta);
        Assert.Equal(2, change.NewStage);
    }

    // Foe-targeting stat drops: Sand Attack (−1 Accuracy) and Tail Whip (−1 Defense).
    [Theory]
    [InlineData("sand-attack", "Accuracy")]
    [InlineData("tail-whip",   "Defense")]
    public async Task LowersFoeStatByOneStage(string moveName, string stat)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.False(result.Has<DamageDealt>(), "a pure stat move deals no damage");

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Defender.Name, change!.CreatureName);   // affects the foe, not the user
        Assert.Equal(stat, change.Stat);
        Assert.Equal(-1, change.Delta);
        Assert.Equal(-1, change.NewStage);
    }
}
