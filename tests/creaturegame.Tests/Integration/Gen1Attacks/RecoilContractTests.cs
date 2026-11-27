using creaturegame.Combat;
using creaturegame.Creatures;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOverkillingRecoilMoveRecoilsAQuarterOfTheHpTheFoeHad(bool capsOverkill)
    {
        var player = TestCreatures.Make("Player", hp: 9999, attack: 300, speed: 200);
        player.AddAttack(Move("double-edge"));

        var log = await FightA41HpFoe(player, "double-edge", capsOverkill);

        int hit = log.Of<DamageDealt>().Single(d => d.TargetName == "Enemy").Damage;
        Assert.True(hit > 41, "the hit overkills");
        Assert.Equal((capsOverkill ? 41 : hit) / 4, log.Of<RecoilDamage>().Single().Damage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFinishingStruggleRecoilsHalfTheHpTheFoeHad(bool capsOverkill)
    {
        var player = TestCreatures.Make("Player", hp: 9999, attack: 300, speed: 200);
        player.AddAttack(Move("tackle"));
        player.MoveSet[0].PowerPointsCurrent = 0;

        var log = await FightA41HpFoe(player, "tackle", capsOverkill);

        int hit = log.Of<DamageDealt>().Single(d => d.TargetName == "Enemy").Damage;
        Assert.True(hit > 41, "the hit overkills");
        Assert.Equal((capsOverkill ? 41 : hit) / 2, log.Of<RecoilDamage>().Single().Damage);
    }

    private Task<RecordingEmitter> FightA41HpFoe(Creature player, string move, bool capsOverkill)
    {
        var enemy = TestCreatures.Make("Enemy", hp: 41, speed: 1);
        enemy.AddAttack(Move("tackle"));
        return FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput(move),
            new ScriptedInput("tackle"),
            capsOverkill ? NoVarianceNoCritHitRules.Instance : FullOverkillDamageRules.Instance
        );
    }
}
