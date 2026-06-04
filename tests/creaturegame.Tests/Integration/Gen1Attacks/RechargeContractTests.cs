using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Recharge moves (Hyper Beam): a successful hit forces the user to spend the next turn recharging
/// (no action). Recharge is checked in <c>AttackAction</c> and driven across turns by <c>Battle</c>,
/// so it's covered both with consecutive actions and in a full battle. A miss deals no damage, so it
/// does not trigger a recharge.
/// </summary>
[Collection(MovesCollection.Name)]
public class RechargeContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task HyperBeamForcesARechargeTurnAfterItHits()
    {
        var turns = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", attack: 150))
            .Defender(TestCreatures.Make("D", hp: 99999, defense: 200))
            .UseRepeated(Move("hyper-beam"), 2);

        Assert.True(turns[0].Has<MoveUsed>());
        Assert.True(turns[0].Has<DamageDealt>());
        // Turn 2 is consumed recharging — no move, no damage.
        Assert.Contains(turns[1].Events, e => e is Recharging r && r.CreatureName == "A");
        Assert.False(turns[1].Has<MoveUsed>());
        Assert.False(turns[1].Has<DamageDealt>());
    }

    [Fact]
    public async Task AMissedHyperBeamDoesNotTriggerRecharge()
    {
        var turns = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Attacker(TestCreatures.Make("A", attack: 150))
            .UseRepeated(Move("hyper-beam"), 2);

        Assert.True(turns[0].Has<MoveMissed>());
        Assert.DoesNotContain(turns[1].Events, e => e is Recharging);
        Assert.True(turns[1].Has<MoveMissed>());   // free to attack again, not recharging
    }

    [Fact]
    public async Task RechargeHappensInAFullBattle()
    {
        // Player out-speeds and Hyper Beams; the enemy is tanky enough to survive past the recharge
        // turn, so the Recharging event is guaranteed to fire before the battle resolves.
        var player = TestCreatures.Make("Player", hp: 9999, attack: 255, speed: 200);
        player.AddAttack(Move("hyper-beam"));
        var enemy = TestCreatures.Make("Enemy", hp: 9999, defense: 100, speed: 1);
        enemy.AddAttack(new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical });

        var emitter = new RecordingEmitter();
        var battle = new Battle(player, enemy, Gen1TypeChart.Instance, AutoSelectInput.Instance,
                                AutoSelectInput.Instance, rules: AlwaysHitRules.Instance, emitter: emitter);
        await battle.StartFightAsync();

        Assert.Contains(emitter.Events, e => e is Recharging r && r.CreatureName == "Player");
    }
}
