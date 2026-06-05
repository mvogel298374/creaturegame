using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Moves whose effect is singular — it doesn't (yet) form a family with other moves. Each gets its
/// own focused contract here until a second move of the same kind justifies promoting it to its own
/// capability class.
/// <list type="bullet">
/// <item><b>Pay Day</b> — deals normal damage and scatters coins = multiplier × user level.</item>
/// <item><b>Whirlwind / Roar / Teleport</b> — end wild battles / force a switch / flee in Gen 1.
/// With no party/run loop yet they are deliberate in-engine no-ops; the contract pins that each is
/// announced but harmless and keeps its Gen 1 −6 priority, so the gap is documented rather than silent.</item>
/// <item><b>Haze / Metronome / Self-Destruct</b> — engine mechanics unit-tested in CoreMechanicsTests;
/// here the <i>real imported rows</i> are driven through <c>AttackAction</c> to prove the mapping works.</item>
/// </list>
/// </summary>
[Collection(MovesCollection.Name)]
public class UniqueMoveEffectContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task PayDayScattersCoinsEqualToMultiplierTimesLevel()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("pay-day"));

        var coins = result.First<CoinsScattered>();
        Assert.NotNull(coins);
        Assert.Equal(Gen1BattleRules.Instance.PayDayCoinMultiplier * 50, coins!.Amount);
        Assert.True(result.Has<DamageDealt>(), "Pay Day also deals damage");
    }

    [Theory]
    [InlineData("whirlwind")]
    [InlineData("roar")]
    [InlineData("teleport")]
    public async Task SwitchMoveIsAnnouncedButHasNoCombatEffect(string moveName)
    {
        var move = Move(moveName);
        var result = await new MoveScenario().Defender(TestCreatures.Make("D", hp: 500)).Use(move);

        Assert.True(result.Has<MoveUsed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(result.Defender.Attributes.MaxHP, result.Defender.Attributes.HP);
        Assert.Equal(StatusCondition.None, result.Defender.Status);
        Assert.Equal(move.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }

    [Theory]
    [InlineData("whirlwind")]
    [InlineData("roar")]
    [InlineData("teleport")]
    public void SwitchMoveHasGen1NegativePriority(string moveName) =>
        Assert.Equal(-6, Move(moveName).Priority);

    // Haze (real imported row) clears every stat stage on both battlers. The escalation math is unit-
    // tested in CoreMechanicsTests; this proves the imported move drives it through AttackAction.
    [Fact]
    public async Task HazeClearsStatStagesOnBothBattlers()
    {
        var attacker = TestCreatures.Make("A");
        attacker.Stages.RaiseAttack(2);
        var defender = TestCreatures.Make("D", hp: 500);
        defender.Stages.RaiseDefense(2);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("haze"));

        Assert.Equal(0, result.Attacker.Stages.Attack);
        Assert.Equal(0, result.Defender.Stages.Defense);
        Assert.True(result.Has<HazeClearedStages>());
    }

    // Metronome (real imported row) calls a move from the pool; a single-move pool makes it deterministic.
    [Fact]
    public async Task MetronomeCallsAMoveFromThePool()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .MovePool(Move("tackle"))
            .Use(Move("metronome"));

        Assert.Contains(result.Events, e => e is MoveUsed m && m.MoveName == "tackle");
        Assert.True(result.Has<DamageDealt>(), "the called move deals damage");
    }

    // Self-Destruct (real imported row) damages the foe and faints the user.
    [Fact]
    public async Task SelfDestructDamagesTheFoeAndFaintsTheUser()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("self-destruct"));

        Assert.True(result.Has<DamageDealt>());
        Assert.True(result.Defender.Attributes.HP < 500);
        Assert.False(result.Attacker.IsAlive(), "the user faints from Self-Destruct");
    }
}
