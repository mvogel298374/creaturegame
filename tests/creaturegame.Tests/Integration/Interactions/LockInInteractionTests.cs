using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for forced move-selection: a rampage (Thrash) keeps the user locked into its
/// move regardless of what the input would pick, and Disabling a creature's only usable move forces
/// it into Struggle. Both exercise the <c>Battle.SelectMoveAsync</c> lock/forced paths end to end.
/// </summary>
[Collection(MovesCollection.Name)]
public class LockInInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    // A locked-in Thrash must keep being used even though the script asks for Tackle on the would-be
    // second choice — Battle bypasses the input while the rampage holds.
    [Fact]
    public async Task RampageKeepsUsingThrashAndIgnoresTheScriptedTackle()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 300,
            speed: 200,
            DamageType.Normal,
            "thrash",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 300, attack: 5, speed: 1, DamageType.Normal, "splash");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("thrash", "tackle") // the "tackle" must never get a turn — Thrash holds
            .EnemyUses("splash")
            .Rules(new ScriptableRules().Deterministic().RampageTurns(3))
            .RunAsync();

        var playerMoves = result.All<MoveUsed>().Where(m => m.AttackerName == "Player").ToList();
        Assert.NotEmpty(playerMoves);
        Assert.All(playerMoves, m => Assert.Equal("thrash", m.MoveName));
        Assert.Equal("Player", result.Winner);
    }

    // Disabling a creature whose only usable move is the disabled one forces Struggle (Battle sees no
    // selectable move and passes null to AttackAction).
    [Fact]
    public async Task DisablingTheOnlyMoveForcesStruggle()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 300,
            speed: 200,
            DamageType.Normal,
            "disable",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 250, attack: 40, speed: 1, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            // Pin the disable duration: RollDisableTurns is otherwise rolled from the rules' own RNG
            // (not the scenario seed), so a full disable (7 turns) keeps the lone move locked for the
            // whole short fight and the Struggle assertion below is deterministic.
            .Rules(new ScriptableRules().Deterministic().DisableTurns(7))
            .PlayerUses("disable", "tackle")
            .EnemyUses("tackle")
            .RunAsync();

        Assert.Contains(
            result.All<MoveDisabled>(),
            d => d.TargetName == "Enemy" && d.MoveName == "tackle"
        );
        // With its lone move disabled, the enemy is forced into Struggle.
        Assert.Contains(
            result.All<MoveUsed>(),
            m => m.AttackerName == "Enemy" && m.MoveName == "Struggle"
        );
    }
}
