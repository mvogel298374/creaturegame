using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;
using creaturegame.Tests.Unit;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>X Accuracy under Gen 1 rules — see GENERATION_SEAMS.md §5.0.2.</summary>
[Collection(MovesCollection.Name)]
public class XAccuracyContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    // Every draw returns its maximum.
    private sealed class MaxRoll : IRandomSource
    {
        public int Calls { get; private set; }

        public int Next(int maxExclusive)
        {
            Calls++;
            return maxExclusive - 1;
        }

        public int Next(int minInclusive, int maxExclusive)
        {
            Calls++;
            return maxExclusive - 1;
        }

        public double NextDouble()
        {
            Calls++;
            return 0.999;
        }
    }

    private static Creature XAccuracyUser(int speed = 100)
    {
        var c = TestCreatures.Make("A", speed: speed);
        c.Battle.UsingXAccuracy = true;
        return c;
    }

    private static Creature MaxEvasionFoe(int speed = 100)
    {
        var c = TestCreatures.Make("D", hp: 500, speed: speed);
        c.Battle.Stages.RaiseEvasion(6);
        return c;
    }

    [Fact]
    public async Task XAccuracyUserHitsOnAMissingRollAgainstMaxedEvasion()
    {
        var without = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .Defender(MaxEvasionFoe())
            .Use(Move("sonic-boom"));
        Assert.True(without.Has<MoveMissed>());

        var with = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .Attacker(XAccuracyUser())
            .Defender(MaxEvasionFoe())
            .Use(Move("sonic-boom"));

        Assert.False(with.Has<MoveMissed>());
        Assert.Equal(Move("sonic-boom").FixedDamageValue, with.TotalDamage);
    }

    [Fact]
    public async Task XAccuracySkipsTheRollWithoutDrawingFromTheRng()
    {
        var rollingRng = new MaxRoll();
        await new MoveScenario()
            .Rules(AlwaysHitRules.Instance)
            .Rng(rollingRng)
            .Use(Move("sonic-boom"));
        Assert.Equal(1, rollingRng.Calls);

        var skippingRng = new MaxRoll();
        var result = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(skippingRng)
            .Attacker(XAccuracyUser())
            .Use(Move("sonic-boom"));

        Assert.True(result.Has<DamageDealt>());
        Assert.Equal(0, skippingRng.Calls);
    }

    [Fact]
    public async Task XAccuracyMakesAFasterOhkoUserLandThroughMaxedEvasion()
    {
        var result = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .Attacker(XAccuracyUser(speed: 200))
            .Defender(MaxEvasionFoe(speed: 50))
            .Use(Move("fissure"));

        Assert.False(result.Has<MoveMissed>());
        Assert.False(result.Defender.IsAlive());
    }

    [Fact]
    public async Task XAccuracyDoesNotRescueAFailedOhkoSpeedCheck()
    {
        var result = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .Attacker(XAccuracyUser(speed: 50))
            .Defender(TestCreatures.Make("D", hp: 500, speed: 200))
            .Use(Move("fissure"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }

    [Fact]
    public async Task XAccuracyDoesNotBeatMist()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.Battle.HasMist = true;

        var result = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .Attacker(XAccuracyUser())
            .Defender(defender)
            .Use(Move("growl"));

        Assert.False(result.Has<MoveMissed>()); // the roll that would miss was skipped...
        Assert.True(result.Has<StatDropBlocked>()); // ...and Mist still stopped the drop
        Assert.Equal(0, result.Defender.Battle.Stages.Attack);
    }

    [Fact]
    public async Task XAccuracyPersistsAcrossTurns_NotAOneShot()
    {
        var rng = new MaxRoll();
        var turns = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(rng)
            .Attacker(XAccuracyUser())
            .Defender(MaxEvasionFoe())
            .UseRepeated(Move("sonic-boom"), 3);

        Assert.All(turns, t => Assert.False(t.Has<MoveMissed>()));
        Assert.All(turns, t => Assert.True(t.Has<DamageDealt>()));
        Assert.Equal(0, rng.Calls);
    }

    [Fact]
    public async Task XAccuracyLetsAMultiHitMoveStrikeItsFullCountOnAMissingRoll()
    {
        var without = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .Defender(MaxEvasionFoe())
            .Use(Move("double-kick"));
        Assert.True(without.Has<MoveMissed>());

        var with = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .Attacker(XAccuracyUser())
            .Defender(MaxEvasionFoe())
            .Use(Move("double-kick"));

        Assert.False(with.Has<MoveMissed>());
        Assert.Equal(2, with.First<MultiHitCompleted>()!.Hits);
    }

    [Fact]
    public async Task XAccuracyAppliesToEveryTurnOfARampage()
    {
        var turns = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .Attacker(XAccuracyUser())
            .Defender(MaxEvasionFoe())
            .UseRepeated(Move("thrash"), 2);

        Assert.All(turns, t => Assert.False(t.Has<MoveMissed>()));
        Assert.All(turns, t => Assert.True(t.Has<DamageDealt>()));
    }

    [Fact]
    public async Task XAccuracyCarriesIntoAMoveCalledByMetronome()
    {
        var result = await new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new MaxRoll())
            .MovePool(Move("sonic-boom"))
            .Attacker(XAccuracyUser())
            .Defender(MaxEvasionFoe())
            .Use(Move("metronome"));

        Assert.False(result.Has<MoveMissed>());
        Assert.True(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task RulesWithoutTheBypassIgnoreTheFlagAndStillRoll()
    {
        var result = await new MoveScenario()
            .Rules(TestAltProfile.Instance.BattleRules)
            .Rng(new MaxRoll())
            .Attacker(XAccuracyUser())
            .Defender(MaxEvasionFoe())
            .Use(Move("sonic-boom"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }
}
