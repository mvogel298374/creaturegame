using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Recoil moves (Take Down, Double-Edge): on a hit the user takes back a fraction of the damage it
/// dealt — Gen 1 is 1/4, via <c>IBattleRules.CalculateRecoilDamage</c> (so the fraction is a gen
/// rule, not a literal). Recoil applies even when the hit KOs the target, and never on a miss
/// (the miss path is covered by <see cref="DamageContractTests"/>).
/// </summary>
[Collection(MovesCollection.Name)]
public class RecoilContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("take-down")]
    [InlineData("double-edge")]
    [InlineData("submission")]
    public async Task UserTakesRecoilProportionalToDamageDealt(string moveName)
    {
        var attacker = TestCreatures.Make("A", hp: 9999, attack: 200);
        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 9999, defense: 80))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>());
        int expected = Gen1BattleRules.Instance.CalculateRecoilDamage(result.TotalDamage);

        var recoil = result.First<RecoilDamage>();
        Assert.NotNull(recoil);
        Assert.Equal(expected, recoil!.Damage);
        Assert.Equal(result.Attacker.Attributes.MaxHP - expected, result.Attacker.Attributes.HP);
        Assert.Equal(result.Attacker.Attributes.HP, recoil.HpAfter);
    }

    [Theory]
    [InlineData("take-down")]
    [InlineData("double-edge")]
    [InlineData("submission")]
    public async Task RecoilStillAppliesWhenTheHitKosTheTarget(string moveName)
    {
        var attacker = TestCreatures.Make("A", hp: 9999, attack: 250);
        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 1, defense: 1))
            .Use(Move(moveName));

        Assert.False(result.Defender.IsAlive());
        Assert.True(result.Has<RecoilDamage>(), "recoil applies even on a KO in Gen 1");
        Assert.True(result.Attacker.Attributes.HP < result.Attacker.Attributes.MaxHP);
    }
}
