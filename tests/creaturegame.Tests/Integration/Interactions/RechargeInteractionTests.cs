using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for the Hyper Beam recharge lock: after a Hyper Beam that deals damage, the user
/// must spend the next turn recharging — it can't act, and the foe gets a free turn. Tested across turns
/// because the skip only manifests on the turn *after* the beam.
/// </summary>
[Collection(MovesCollection.Name)]
public class RechargeInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    // Gen 1: every damaging Hyper Beam is followed by exactly one forced recharge turn — except the one
    // that lands the finishing blow, since the battle ends before that recharge turn arrives. So over a
    // whole fight, recharge turns == Hyper Beams used − 1. (Enemy HP is tuned so a beam lands the kill
    // while Hyper Beam still has PP — letting it run out would finish the fight with Struggle instead.)
    [Fact]
    public async Task EachDamagingHyperBeamForcesExactlyOneRechargeTurnExceptTheKill()
    {
        // Player spams Hyper Beam; the foe survives two beams and dies to the third, only chipping back,
        // so the fight runs beam → recharge → beam → recharge → beam(kill).
        var player = Mon(
            "Player",
            hp: 9999,
            attack: 80,
            speed: 200,
            DamageType.Normal,
            "hyper-beam"
        );
        var enemy = Mon("Enemy", hp: 200, attack: 5, speed: 1, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("hyper-beam")
            .EnemyUses("tackle")
            .RunAsync();

        int beams = result
            .All<MoveUsed>()
            .Count(m => m.AttackerName == "Player" && m.MoveName == "hyper-beam");
        int recharges = result.All<Recharging>().Count(r => r.CreatureName == "Player");

        Assert.True(beams >= 2, $"expected several Hyper Beams across the fight (saw {beams})");
        Assert.Equal(beams - 1, recharges);
        // The foe still got to act on every recharge turn — recharge is a forfeited turn, not a free pass.
        Assert.NotEmpty(result.DamageTo("Player"));
        Assert.Equal("Player", result.Winner);
    }
}
