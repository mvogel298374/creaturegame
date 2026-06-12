using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for Gen 1 binding moves (Wrap, Bind, Clamp, Fire Spin). Gen 1 partial trapping locks the
/// BINDER into re-using the move every turn — dealing its damage — while the trapped foe can't act ("neither
/// the user nor the target will be able to select moves") and takes NO residual chip. So a faster binder traps
/// the foe from turn 1, grinds it down with the move alone, never switches to another move, and never lets the
/// foe act. (Source: Bulbapedia, Bind — Generation I effect.)
/// </summary>
[Collection(MovesCollection.Name)]
public class BindingInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    [Fact]
    public async Task WrapLocksTheBinderIntoTheMoveAndTrapsTheFoeSoItNeverActs()
    {
        // The binder is faster and knows Wrap + Tackle, and is SCRIPTED to use Tackle after the first Wrap.
        // The Gen 1 lock must override that script — the binder is forced to keep Wrapping — so Tackle never
        // appears. The foe is tuned to die from Wrap alone (no residual chip exists) before the 5-turn bind ends.
        var player = Mon(
            "Player",
            hp: 999,
            attack: 200,
            speed: 200,
            DamageType.Normal,
            "wrap",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 60, attack: 80, speed: 1, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap", "tackle") // 2nd choice is Tackle — the lock overrides it
            .EnemyUses("tackle")
            .Rules(new ScriptableRules().Deterministic().BindingTurns(5))
            .RunAsync();

        // The foe was trapped from the start...
        Assert.Contains(result.All<BindingStarted>(), b => b.TargetName == "Enemy");
        // ...blocked every turn it would have acted, and never got a move off.
        Assert.True(result.Count<BindingBlocked>() >= 2);
        Assert.DoesNotContain(result.All<MoveUsed>(), m => m.AttackerName == "Enemy");

        // The binder was LOCKED into Wrap: it Wrapped on multiple turns and never used the scripted Tackle.
        var playerMoves = result.All<MoveUsed>().Where(m => m.AttackerName == "Player").ToList();
        Assert.True(playerMoves.Count(m => m.MoveName == "wrap") >= 2);
        Assert.DoesNotContain(playerMoves, m => m.MoveName == "tackle");

        Assert.Equal("Player", result.Winner);
    }
}
