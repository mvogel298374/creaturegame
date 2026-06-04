using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Drain moves (Absorb, Mega Drain): deal normal damage and heal the user for half the damage dealt
/// (<c>DamageCategory.Drain</c> + <c>DrainPercent</c>). Healing never overflows past max HP. Runs on
/// the real damage path under deterministic (no-variance, no-crit) rules so the half-of-damage maths
/// is exact.
/// </summary>
[Collection(MovesCollection.Name)]
public class DrainContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("absorb")]
    [InlineData("mega-drain")]
    public async Task HealsHalfTheDamageDealt(string moveName)
    {
        var attacker = TestCreatures.Make("A", special: 120);
        attacker.Attributes.HP = 1;   // leave room to heal so the gain is visible

        var result = await new MoveScenario()
            .Rules(NoVarianceNoCritHitRules.Instance)
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 500, special: 80))
            .Use(Move(moveName));

        int dealt = result.TotalDamage;
        Assert.True(dealt > 0, "a drain move still deals damage");

        var heal = result.First<DrainHealed>();
        Assert.NotNull(heal);
        Assert.Equal(System.Math.Max(1, dealt / 2), heal!.HealAmount);
        Assert.Equal(1 + heal.HealAmount, result.Attacker.Attributes.HP);
    }

    [Fact]
    public async Task DoesNotHealAboveMaxHp()
    {
        var attacker = TestCreatures.Make("A", hp: 300, special: 120);   // already at full HP

        var result = await new MoveScenario()
            .Rules(NoVarianceNoCritHitRules.Instance)
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 500, special: 80))
            .Use(Move("mega-drain"));

        Assert.True(result.Has<DamageDealt>());
        Assert.Equal(300, result.Attacker.Attributes.HP);   // capped, no overflow
    }
}
