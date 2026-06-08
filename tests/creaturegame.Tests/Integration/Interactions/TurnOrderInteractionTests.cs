using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for how a status condition reshapes turn order across turns. Gen 1 quarters a
/// paralysed battler's effective Speed, which can flip who moves first — the kind of emergent outcome
/// only a multi-turn battle (not a single action) exposes.
/// </summary>
[Collection(MovesCollection.Name)]
public class TurnOrderInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    // Gen 1: paralysis cuts effective Speed to a quarter. A naturally faster battler that gets paralysed
    // can be outsped by a slower foe from then on — here the slow foe paralyses the speedster on turn 1,
    // then out-speeds and OHKOs it on turn 2 before it can move again. The fast mon lands exactly one
    // move (its turn-1 action); the slower foe wins purely because the paralysis flipped the order.
    [Fact]
    public async Task ParalysisQuartersSpeedAndFlipsTurnOrder()
    {
        // Player is twice as fast but frail; the slow foe paralyses then finishes it.
        var player = Mon("Player", hp: 100, attack: 5, speed: 200, DamageType.Normal, "splash");
        player.Attributes.Defense = 20;
        var enemy = Mon(
            "Enemy",
            hp: 999,
            attack: 999,
            speed: 100,
            DamageType.Normal,
            "thunder-wave",
            "tackle"
        );

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("splash")
            .EnemyUses("thunder-wave", "tackle")
            .RunAsync();

        // Paralysis landed on the speedster…
        Assert.Contains(
            result.All<StatusApplied>(),
            s => s.TargetName == "Player" && s.Status == StatusCondition.Paralysis
        );
        // …and from turn 2 the slower foe moved first and KO'd it, so the player only ever got one move
        // off (the turn-1 Splash) before fainting to a base-slower opponent.
        Assert.Equal(1, result.All<MoveUsed>().Count(m => m.AttackerName == "Player"));
        Assert.False(player.IsAlive());
        Assert.Equal("Enemy", result.Winner);
    }
}
