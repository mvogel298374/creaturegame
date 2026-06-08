using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for Bide: the user commits for a fixed number of turns, banking every hit it takes
/// (any damage category), then unleashes <b>double</b> the stored total at the foe on release. The
/// across-turns accumulation is the whole point — a single-action test can't reach it.
/// </summary>
[Collection(MovesCollection.Name)]
public class BideInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    // Gen 1: Bide's release damage equals 2× everything the user took while storing. The player is faster
    // so on the release turn it unleashes before that turn's tackle — making every player-damage event
    // recorded before the unleash a stored hit, and the unleash exactly double their sum.
    [Fact]
    public async Task BideUnleashesDoubleTheDamageStoredWhileCommitted()
    {
        var player = Mon("Player", hp: 9999, attack: 5, speed: 200, DamageType.Normal, "bide");
        var enemy = Mon("Enemy", hp: 9999, attack: 60, speed: 1, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("bide")
            .EnemyUses("tackle")
            .Rules(new ScriptableRules().Deterministic().BideTurns(2))
            .RunAsync();

        Assert.Contains(result.All<BideStoring>(), e => e.CreatureName == "Player");

        // Bide is the player's only damaging move, so the first damage dealt to the foe is the unleash.
        var events = result.Events.ToList();
        int unleashIdx = events.FindIndex(e => e is DamageDealt { TargetName: "Enemy" });
        Assert.True(unleashIdx >= 0, "Bide never unleashed on the foe");

        int stored = events
            .Take(unleashIdx)
            .OfType<DamageDealt>()
            .Where(d => d.TargetName == "Player")
            .Sum(d => d.Damage);
        int unleash = ((DamageDealt)events[unleashIdx]).Damage;

        Assert.True(stored > 0, "expected the player to bank some damage while biding");
        Assert.Equal(stored * 2, unleash);
    }
}
