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
/// <item><b>Whirlwind</b> — ends wild battles / forces a switch in Gen 1. With no party/run loop
/// yet it is a deliberate in-engine no-op; the contract pins that it is announced but harmless and
/// keeps its Gen 1 −6 priority, so the gap is documented rather than silent.</item>
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

    [Fact]
    public async Task WhirlwindIsAnnouncedButHasNoCombatEffect()
    {
        var move = Move("whirlwind");
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(move);

        Assert.True(result.Has<MoveUsed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(result.Defender.Attributes.MaxHP, result.Defender.Attributes.HP);
        Assert.Equal(StatusCondition.None, result.Defender.Status);
        Assert.Equal(move.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }

    [Fact]
    public void WhirlwindHasGen1NegativePriority()
        => Assert.Equal(-6, Move("whirlwind").Priority);
}
