using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Gen 1 type-based immunities — a move can hit yet do nothing because the target's type is immune
/// to the effect. These are gen-variable rules on the <see cref="IBattleRules"/> seam
/// (status/Leech-Seed immunity) and the <see cref="ITypeChart"/> (0× damage for moves that bypass
/// the normal calc). Each case emits <see cref="MoveHadNoEffect"/> for the "It doesn't affect …" line,
/// except a damaging move whose <i>secondary</i> status is blocked (the hit still lands).
/// </summary>
[Collection(MovesCollection.Name)]
public class ImmunityContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task PoisonTypesAreImmuneToPoisonPowder()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Poison, hp: 500))
            .Use(Move("poison-powder"));

        Assert.Equal(StatusCondition.None, result.Defender.Status);
        Assert.True(result.Has<MoveHadNoEffect>());
    }

    [Fact]
    public async Task GrassTypesAreImmuneToLeechSeed()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Grass, hp: 500))
            .Use(Move("leech-seed"));

        Assert.False(result.Defender.HasLeechSeed);
        Assert.True(result.Has<MoveHadNoEffect>());
    }

    [Fact]
    public async Task BodySlamCannotParalyzeNormalTypesButStillDamages()
    {
        // Gen 1 quirk: Body Slam (a Normal move) can't paralyze a Normal-type — but the hit lands.
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("D", type1: DamageType.Normal, hp: 500))
            .Use(Move("body-slam"));

        Assert.True(result.Has<DamageDealt>());
        Assert.Equal(StatusCondition.None, result.Defender.Status);
        Assert.False(result.Has<MoveHadNoEffect>(), "the move connected; only the secondary status was blocked");
    }

    [Fact]
    public async Task GhostTypesAreImmuneToSeismicToss()
    {
        // Seismic Toss is Fighting-type; Ghost is immune (0×), so even level-based damage does nothing.
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500))
            .Use(Move("seismic-toss"));

        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
        Assert.True(result.Has<MoveHadNoEffect>());
    }

    [Fact]
    public async Task GroundTypesAreImmuneToThunderWave()
    {
        // Thunder Wave is Electric; Ground is immune (0×), so even this pure-status move does nothing.
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Ground, hp: 500))
            .Use(Move("thunder-wave"));

        Assert.Equal(StatusCondition.None, result.Defender.Status);
        Assert.True(result.Has<MoveHadNoEffect>());
    }

    [Fact]
    public async Task FlyingTypesAreImmuneToFissure()
    {
        // Fissure is Ground (an OHKO move); Flying is immune (0×).
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Flying, hp: 500))
            .Use(Move("fissure"));

        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
        Assert.True(result.Has<MoveHadNoEffect>());
    }

    [Fact]
    public async Task GhostTypesAreImmuneToCounter()
    {
        // Counter is Fighting-type; a Ghost target is immune even when the user took counterable damage.
        var attacker = TestCreatures.Make("A");
        attacker.LastDamageTaken = 50;
        attacker.LastDamageType  = DamageType.Fighting;

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500))
            .Use(Move("counter"));

        Assert.False(result.Has<DamageDealt>());
        Assert.True(result.Has<MoveHadNoEffect>());
    }
}
