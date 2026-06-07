using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for Gen 1 Counter's quirks: the "last damage taken" value persists across turns
/// (Counter can answer a previous turn's hit even when nothing connected this turn), and against a
/// multi-hit move Counter answers the <i>last single strike</i>, never the accumulated total.
/// </summary>
[Collection(MovesCollection.Name)]
public class CounterInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    // Gen 1 quirk: the last-damage value isn't cleared between turns. The foe hits on turn 1, does
    // nothing on turn 2, and Counter (resolving last via −5 priority) still returns 2× the turn-1 hit.
    [Fact]
    public async Task CounterAnswersAPreviousTurnsDamageWhenNothingHitThisTurn()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 5,
            speed: 1,
            DamageType.Normal,
            "splash",
            "counter"
        );
        var enemy = Mon(
            "Enemy",
            hp: 999,
            attack: 100,
            speed: 200,
            DamageType.Normal,
            "tackle",
            "splash"
        );

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("splash", "counter") // counter from turn 2 on
            .EnemyUses("tackle", "splash") // hits once on turn 1, then never again
            .RunAsync();

        int tackle = result.DamageTo("Player")[0].Damage; // the only hit the player ever took
        var counterHits = result.DamageTo("Enemy");
        Assert.NotEmpty(counterHits);
        // The turn-2 Counter answered the stale turn-1 tackle (foe only splashed on turn 2).
        Assert.Equal(tackle * 2, counterHits[0].Damage);
    }

    // Gen 1: against a multi-hit move Counter returns 2× the LAST strike, not 2× the sum of all strikes.
    [Fact]
    public async Task CounterAnswersTheLastHitOfAMultiHitMoveNotTheSum()
    {
        var player = Mon("Player", hp: 999, attack: 5, speed: 1, DamageType.Normal, "counter");
        var enemy = Mon("Enemy", hp: 999, attack: 80, speed: 200, DamageType.Normal, "double-slap");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("counter")
            .EnemyUses("double-slap")
            .Rules(new ScriptableRules().Deterministic().MultiHits(3)) // 3 equal strikes per turn
            .RunAsync();

        var slaps = result.DamageTo("Player");
        Assert.True(slaps.Count >= 3, "expected a 3-hit Double Slap");
        int oneSlap = slaps[0].Damage;
        var counterHits = result.DamageTo("Enemy");
        Assert.NotEmpty(counterHits);
        // 2× one strike, NOT 2× (3 strikes).
        Assert.Equal(oneSlap * 2, counterHits[0].Damage);
        Assert.NotEqual(oneSlap * 3 * 2, counterHits[0].Damage);
    }
}
