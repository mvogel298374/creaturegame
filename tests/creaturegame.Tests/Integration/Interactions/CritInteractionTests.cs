using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// The famous Gen 1 critical-hit quirk: a crit ignores stat-stage modifiers entirely (it recomputes
/// damage from the unmodified stats). So a +2 Attack boost the attacker built up does NOT increase a
/// critical hit — the crit before and after Swords Dance deals identical damage.
/// </summary>
[Collection(MovesCollection.Name)]
public class CritInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    [Fact]
    public async Task Gen1CritIgnoresTheAttackersAttackStageBoost()
    {
        var attacker = Mon(
            "Player",
            hp: 999,
            attack: 50,
            speed: 200,
            DamageType.Normal,
            "tackle",
            "swords-dance"
        );
        var target = Mon("Enemy", hp: 220, attack: 5, speed: 1, DamageType.Normal, "splash");

        var result = await new BattleScenario()
            .Player(attacker)
            .Enemy(target)
            // crit on, no variance: the only thing that could move the number is the stage boost.
            .Rules(new ScriptableRules().AlwaysHit().AlwaysCrit().NoVariance())
            .PlayerUses("tackle", "swords-dance", "tackle") // crit, +2 Atk, crit again
            .EnemyUses("splash")
            .RunAsync();

        var crits = result.DamageTo("Enemy");
        Assert.True(crits.Count >= 2, "need both crits recorded");
        Assert.All(crits.Take(2), d => Assert.True(d.IsCrit));
        // Gen 1: the +2 Swords Dance between the two crits is ignored — identical damage.
        Assert.Equal(crits[0].Damage, crits[1].Damage);
    }
}
