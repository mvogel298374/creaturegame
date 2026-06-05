using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Pure status moves afflict the target without dealing damage. Sing inflicts Sleep (a major
/// <see cref="StatusCondition"/>); Supersonic inflicts confusion (a separate per-battle counter).
/// Neither deals damage, and neither effect lands when the move misses.
/// </summary>
[Collection(MovesCollection.Name)]
public class StatusMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task SingPutsTheTargetToSleepWithoutDamage()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("sing"));

        Assert.False(result.Has<DamageDealt>(), "Sing is a status move — no damage");
        Assert.Equal(StatusCondition.Sleep, result.Defender.Status);
        Assert.True(result.Defender.SleepTurns > 0);
        Assert.Contains(
            result.Events,
            e => e is StatusApplied s && s.Status == StatusCondition.Sleep
        );
    }

    [Fact]
    public async Task SingAppliesNoSleepWhenItMisses()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("sing"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(StatusCondition.None, result.Defender.Status);
    }

    // The powders: Poison Powder (Poison), Stun Spore (Paralysis), Sleep Powder (Sleep) — pure
    // status moves that afflict a (non-immune) target without dealing damage.
    [Theory]
    [InlineData("poison-powder", StatusCondition.Poison)]
    [InlineData("stun-spore", StatusCondition.Paralysis)]
    [InlineData("sleep-powder", StatusCondition.Sleep)]
    public async Task PowderInflictsItsStatusWithoutDamage(
        string moveName,
        StatusCondition expected
    )
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move(moveName));

        Assert.False(result.Has<DamageDealt>(), "a powder is a status move — no damage");
        Assert.Equal(expected, result.Defender.Status);
        Assert.Contains(result.Events, e => e is StatusApplied);
    }

    [Theory]
    [InlineData("poison-powder")]
    [InlineData("stun-spore")]
    [InlineData("sleep-powder")]
    public async Task PowderAppliesNothingOnMiss(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(StatusCondition.None, result.Defender.Status);
    }

    [Fact]
    public async Task ThunderWaveParalyzesTheTarget()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move("thunder-wave"));

        Assert.False(result.Has<DamageDealt>(), "Thunder Wave is a status move — no damage");
        Assert.Equal(StatusCondition.Paralysis, result.Defender.Status);
        Assert.Contains(result.Events, e => e is StatusApplied);
    }

    [Fact]
    public async Task HypnosisPutsTheTargetToSleepWithoutDamage()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("hypnosis"));

        Assert.False(result.Has<DamageDealt>(), "Hypnosis is a status move — no damage");
        Assert.Equal(StatusCondition.Sleep, result.Defender.Status);
        Assert.True(result.Defender.SleepTurns > 0);
        Assert.Contains(
            result.Events,
            e => e is StatusApplied s && s.Status == StatusCondition.Sleep
        );
    }

    // Toxic badly-poisons (BadPoison), distinct from the regular Poison its modern PokeAPI ailment
    // reports — this also exercises the importer's Gen 1 override (pinned in
    // SecondaryChanceDataContractTests). The escalating damage itself is covered in CoreMechanicsTests.
    [Fact]
    public async Task ToxicBadlyPoisonsTheTarget()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move("toxic"));

        Assert.False(result.Has<DamageDealt>(), "Toxic is a status move — no damage");
        Assert.Equal(StatusCondition.BadPoison, result.Defender.Status);
        Assert.Contains(
            result.Events,
            e => e is StatusApplied s && s.Status == StatusCondition.BadPoison
        );
    }

    [Fact]
    public async Task ToxicAppliesNoStatusOnMiss()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move("toxic"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(StatusCondition.None, result.Defender.Status);
    }

    // Glare (Normal → Paralysis) and Poison Gas (Poison → Poison): pure status moves that afflict a
    // non-immune target without dealing damage, and apply nothing on a miss.
    [Theory]
    [InlineData("glare", StatusCondition.Paralysis)]
    [InlineData("poison-gas", StatusCondition.Poison)]
    public async Task InflictsItsStatusWithoutDamage(string moveName, StatusCondition expected)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move(moveName));

        Assert.False(result.Has<DamageDealt>(), $"{moveName} is a status move — no damage");
        Assert.Equal(expected, result.Defender.Status);
        Assert.Contains(result.Events, e => e is StatusApplied);
    }

    [Theory]
    [InlineData("glare")]
    [InlineData("poison-gas")]
    public async Task InflictsNothingOnMiss(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(StatusCondition.None, result.Defender.Status);
    }

    // Pure confusion moves (no damage): Supersonic (Normal) and Confuse Ray (Ghost).
    [Theory]
    [InlineData("supersonic")]
    [InlineData("confuse-ray")]
    public async Task ConfusesTheTargetWithoutDamage(string moveName)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move(moveName));

        Assert.False(result.Has<DamageDealt>(), $"{moveName} is a status move — no damage");
        Assert.True(result.Defender.ConfusedTurns > 0);
        Assert.Contains(result.Events, e => e is ConfusionStarted);
    }

    [Theory]
    [InlineData("supersonic")]
    [InlineData("confuse-ray")]
    public async Task DoesNotConfuseOnMiss(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, result.Defender.ConfusedTurns);
    }
}
