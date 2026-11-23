using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>Full-battle probes for Gen 1 Counter's quirks; GENERATION_SEAMS.md §2 (Counter).</summary>
[Collection(MovesCollection.Name)]
public class CounterInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    // The foe hits on turn 1 and Splashes after; pokered's 0-power check makes the turn-2 Counter miss.
    [Fact]
    public async Task CounterMissesWhenTheFoeUsedAZeroPowerMoveAfterAnEarlierHit()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 200,
            speed: 1,
            DamageType.Normal,
            "splash",
            "counter",
            "tackle"
        );
        var enemy = Mon(
            "Enemy",
            hp: 20,
            attack: 100,
            speed: 200,
            DamageType.Normal,
            "tackle",
            "splash"
        );

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("splash", "counter", "tackle")
            .EnemyUses("tackle", "splash")
            .RunAsync();

        Assert.Single(result.All<MoveMissed>(), m => m.AttackerName == "Player");
        Assert.Single(result.DamageTo("Enemy")); // only the finishing Tackle
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
