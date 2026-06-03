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
}
