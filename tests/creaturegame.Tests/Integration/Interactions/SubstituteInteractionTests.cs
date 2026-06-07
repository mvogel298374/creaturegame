using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.Integration.Gen1Attacks;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for how Substitute interacts with other mechanics across turns — the kind of
/// emergent behaviour the single-action contract tests can't reach. Each asserts a documented Gen 1
/// truth; a failure is a real interaction bug, not a style nit.
/// </summary>
[Collection(MovesCollection.Name)]
public class SubstituteInteractionTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    // Gen 1: damage your own Substitute soaks is NOT damage *you* took, so Counter has nothing to
    // answer — it must fail even though the foe just hit (the decoy) with a Normal move.
    [Fact]
    public async Task CounterCannotAnswerDamageAbsorbedByOwnSubstitute()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 200,
            speed: 200,
            "substitute",
            "counter",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 50, attack: 40, speed: 1, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("substitute", "counter", "tackle")
            .EnemyUses("tackle")
            .RunAsync();

        // The turn-2 Counter found no counterable damage (the tackle hit the substitute) and missed.
        Assert.Contains(result.All<MoveMissed>(), m => m.MoveName == "counter");
        // Counter therefore never damaged the enemy — its only damage came from the finishing tackle.
        Assert.Equal("Player", result.Winner);
    }

    // Gen 1: a Substitute shields the user from the opponent's status moves — Thunder Wave can't
    // paralyse through a standing decoy.
    [Fact]
    public async Task SubstituteBlocksOpponentParalysis()
    {
        var enemy = Mon("Enemy", hp: 50, attack: 10, speed: 200, "substitute", "splash");
        var player = Mon("Player", hp: 999, attack: 200, speed: 1, "thunder-wave", "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .EnemyUses("substitute", "splash")
            .PlayerUses("thunder-wave", "tackle")
            .RunAsync();

        Assert.DoesNotContain(
            result.All<StatusApplied>(),
            s => s.TargetName == "Enemy" && s.Status == StatusCondition.Paralysis
        );
        Assert.NotEqual(StatusCondition.Paralysis, enemy.Status);
    }

    // English RBY quirk: unlike status / stat-drops / confusion, Leech Seed lands THROUGH a Substitute
    // in the localized Gen 1 games (it's blocked only in the Japanese games and Stadium; Gen 2+ blocks
    // it everywhere). This engine targets English RBY, so the seed takes hold and drains. Pins the
    // behaviour so a future "make Leech Seed consistent with the other sub-shielded effects" change
    // can't silently break Gen 1 fidelity.
    [Fact]
    public async Task LeechSeedLandsThroughSubstitute_EnglishRbyQuirk()
    {
        var enemy = Mon("Enemy", hp: 50, attack: 10, speed: 200, "substitute", "splash");
        var player = Mon("Player", hp: 999, attack: 200, speed: 1, "leech-seed", "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .EnemyUses("substitute", "splash")
            .PlayerUses("leech-seed", "tackle")
            .RunAsync();

        Assert.Contains(result.All<LeechSeedApplied>(), e => e.TargetName == "Enemy");
        Assert.NotEmpty(result.DamageTo("Enemy")); // seed (and the finisher) reached the real creature
        Assert.Contains(result.All<LeechSeedDamage>(), d => d.DrainedName == "Enemy");
    }

    private Creature Mon(string name, int hp, int attack, int speed, params string[] moveNames)
    {
        var c = TestCreatures.Make(name, hp: hp, attack: attack, speed: speed);
        foreach (var n in moveNames)
            c.AddAttack(Move(n));
        return c;
    }
}
