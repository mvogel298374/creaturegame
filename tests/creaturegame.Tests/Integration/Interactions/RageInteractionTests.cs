using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for Rage: once used, the move auto-repeats (a lock-in), and every hit the rager
/// takes on the standard damage path raises its Attack stage. The escalation only starts after Rage is
/// in effect — a hit landed before the rager committed doesn't count — and it stacks across turns.
/// </summary>
[Collection(MovesCollection.Name)]
public class RageInteractionTests(MovesFixture moves) : InteractionTest(moves)
{
    // Gen 1: a creature locked into Rage gains an Attack stage each time it's struck. Over several
    // enemy hits its Attack rises step by step (+1, +2, +3, …) — a strictly increasing sequence.
    [Fact]
    public async Task RagingCreaturesAttackRisesEachTimeItIsHit()
    {
        // Player rages (slow, so the foe always hits it first), foe keeps tackling. Both are bulky so
        // the battle lasts long enough to bank several attack-up steps before anyone faints.
        var player = Mon("Player", hp: 9999, attack: 40, speed: 1, DamageType.Normal, "rage");
        var enemy = Mon("Enemy", hp: 9999, attack: 60, speed: 200, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("rage")
            .EnemyUses("tackle")
            .RunAsync();

        var rises = result
            .All<StatStageChanged>()
            .Where(s => s.CreatureName == "Player" && s.Stat == StageStat.Attack.ToString())
            .ToList();

        Assert.True(
            rises.Count >= 2,
            $"expected Rage to raise Attack repeatedly (saw {rises.Count})"
        );
        Assert.All(rises, r => Assert.True(r.Delta > 0)); // every Rage trigger is an increase
        // Each hit lifts Attack one stage, climbing from +1 up to the Gen 1 +6 ceiling and then holding
        // there (further hits keep firing the event but can't push past the cap).
        var climb = rises.Select(r => r.NewStage).Distinct().ToList();
        Assert.Equal(Enumerable.Range(1, 6), climb);
    }
}
