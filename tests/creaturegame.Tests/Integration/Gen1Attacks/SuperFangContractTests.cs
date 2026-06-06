using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Super Fang (Gen 1): deals damage equal to half the target's <i>current</i> HP (floor, minimum 1),
/// ignoring base power, the attacker/defender stats, and the (non-zero) type matchup. The Normal-type
/// 0× immunity (Ghost) is covered by the shared immunity tests; here we pin the halving and that bulk
/// and a non-immune type don't change it.
/// </summary>
[Collection(MovesCollection.Name)]
public class SuperFangContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData(300, 150)]
    [InlineData(201, 100)] // floor(201/2)
    [InlineData(1, 1)] // minimum 1
    public async Task DealsHalfTheTargetsCurrentHp(int hp, int expected)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: hp, defense: 250, special: 250))
            .Use(Move("super-fang"));

        Assert.Equal(expected, result.TotalDamage);
    }

    // Half-HP ignores the defender's bulk and the (non-zero) type matchup — a beefy Water wall still
    // loses exactly half its current HP.
    [Fact]
    public async Task IgnoresDefenderBulkAndNonImmuneType()
    {
        var result = await new MoveScenario()
            .Defender(
                TestCreatures.Make("D", type1: DamageType.Water, hp: 400, defense: 1, special: 1)
            )
            .Use(Move("super-fang"));

        Assert.Equal(200, result.TotalDamage);
    }
}
