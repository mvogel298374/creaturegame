using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Dream Eater only works on a <b>sleeping</b> target: against a sleeper it deals damage and drains
/// half of it back to the user; against anything awake it fails (no damage, no heal). The sleep
/// requirement is invariant across generations, so it's enforced in <c>AttackAction</c> rather than on
/// the rules seam; the 50% recovery rides on the normal <see cref="DamageCategory.Drain"/> path.
/// </summary>
[Collection(MovesCollection.Name)]
public class DreamEaterContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task DrainsHpFromASleepingTarget()
    {
        var attacker = TestCreatures.Make("A", special: 200);
        attacker.Attributes.HP = 50; // damaged, so the drain heal is observable
        var defender = TestCreatures.Make("D", hp: 500, special: 60);
        defender.Status = StatusCondition.Sleep;

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("dream-eater"));

        Assert.True(result.Has<DamageDealt>(), "Dream Eater damages a sleeping target");
        Assert.True(result.TotalDamage > 0);
        Assert.True(result.Defender.Attributes.HP < 500);

        Assert.True(result.Has<DrainHealed>(), "half the damage is drained back");
        Assert.True(result.Attacker.Attributes.HP > 50, "the user recovered HP from the drain");
    }

    [Fact]
    public async Task FailsAgainstAnAwakeTarget()
    {
        var attacker = TestCreatures.Make("A", special: 200);
        attacker.Attributes.HP = 50;
        var defender = TestCreatures.Make("D", hp: 500); // awake (Status None)

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("dream-eater"));

        Assert.True(result.Has<MoveMissed>(), "Dream Eater fails on a non-sleeping target");
        Assert.False(result.Has<DamageDealt>());
        Assert.False(result.Has<DrainHealed>());
        Assert.Equal(500, result.Defender.Attributes.HP); // target untouched
        Assert.Equal(50, result.Attacker.Attributes.HP); // user gains nothing
    }
}
