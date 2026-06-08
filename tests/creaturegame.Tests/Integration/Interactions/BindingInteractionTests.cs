using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for binding moves (Wrap, Bind, Clamp, Fire Spin): the trapped foe loses its turn
/// for the whole bind and is chipped at end of turn. The lock spans multiple turns, so only a full
/// battle — not a single action — shows the foe being denied its move turn after turn.
/// </summary>
[Collection(MovesCollection.Name)]
public class BindingInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    // Gen 1: while Wrap's bind holds, the trapped creature can't act and takes 1/16-max-HP chip each
    // turn. The wrapper is faster and locked into Wrap, so the foe is trapped from turn 1 and — with the
    // bind pinned to its full 5-turn length — never gets a single move off before the chip + Wrap damage
    // faint it. Mirrors the flinch-lock probe: assert the foe is repeatedly blocked and never acts.
    [Fact]
    public async Task WrapTrapsTheFoeSoItNeverGetsAMoveOff()
    {
        var player = Mon("Player", hp: 999, attack: 200, speed: 200, DamageType.Normal, "wrap");
        var enemy = Mon("Enemy", hp: 150, attack: 80, speed: 1, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap")
            .EnemyUses("tackle")
            .Rules(new ScriptableRules().Deterministic().BindingTurns(5))
            .RunAsync();

        Assert.Contains(result.All<BindingStarted>(), b => b.TargetName == "Enemy");
        // Trapped every turn it tried to act, and chipped by the bind each end-of-turn.
        Assert.True(result.Count<BindingBlocked>() >= 2);
        Assert.Contains(result.All<BindingDamage>(), d => d.TargetName == "Enemy");
        // Never escaped the lock to land a move; faints still bound.
        Assert.DoesNotContain(result.All<MoveUsed>(), m => m.AttackerName == "Enemy");
        Assert.Equal("Player", result.Winner);
    }
}
