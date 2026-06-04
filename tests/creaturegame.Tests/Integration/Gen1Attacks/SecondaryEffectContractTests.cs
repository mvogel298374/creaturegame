using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Damaging moves whose secondary effect is <i>not</i> a major status (those live in
/// <see cref="SecondaryStatusContractTests"/>): a chance-based stat drop on the foe (Acid lowers
/// Defense in Gen 1) and a chance-based confusion (Psybeam). Each lands on hit when the secondary
/// roll succeeds and never on a miss. The chance is gated through
/// <see cref="IBattleRules.GetSecondaryEffectChance"/>, so <see cref="ForceSecondaryRules"/> forces it.
/// </summary>
[Collection(MovesCollection.Name)]
public class SecondaryEffectContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task AcidCanLowerTheFoesDefenseAsASecondaryEffect()
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move("acid"));

        Assert.True(result.Has<DamageDealt>(), "Acid deals damage");
        Assert.Equal(-1, result.Defender.Stages.Defense);
        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal(result.Defender.Name, change!.CreatureName);
        Assert.Equal("Defense", change.Stat);
        Assert.Equal(-1, change.Delta);
    }

    [Fact]
    public async Task AcidDoesNotLowerDefenseOnMiss()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move("acid"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, result.Defender.Stages.Defense);
    }

    [Fact]
    public async Task PsybeamCanConfuseAsASecondaryEffect()
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move("psybeam"));

        Assert.True(result.Has<DamageDealt>(), "Psybeam deals damage");
        Assert.True(result.Defender.ConfusedTurns > 0);
        Assert.Contains(result.Events, e => e is ConfusionStarted);
    }

    [Fact]
    public async Task PsybeamDoesNotConfuseOnMiss()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move("psybeam"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, result.Defender.ConfusedTurns);
    }
}
