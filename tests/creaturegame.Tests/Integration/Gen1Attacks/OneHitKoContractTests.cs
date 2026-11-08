using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// One-hit KO moves (Guillotine, Horn Drill): on hit they deal the target's full remaining HP and
/// fell it; they <b>fail</b> (not merely miss) when the target out-speeds the user (the Gen 1 rule —
/// a Speed comparison, not the level check Gen 2 introduced); and they still miss on a failed
/// accuracy roll. Damage runs on the real <c>DamageCategory.OHKO</c> branch — the default rules
/// double only removes the 1/256 accuracy fluke.
/// </summary>
[Collection(MovesCollection.Name)]
public class OneHitKoContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("guillotine")]
    [InlineData("horn-drill")]
    [InlineData("fissure")]
    public async Task DealsFullHpDamageAndFells(string moveName)
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", level: 50, hp: 500, defense: 250))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>());
        Assert.Equal(500, result.TotalDamage);
        Assert.Equal(0, result.Defender.Attributes.HP);
        Assert.False(result.Defender.IsAlive());
    }

    [Fact]
    public async Task ALandedOhkoPrintsItsLineAndNeverTheCritLine()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("guillotine"));

        var hit = result.First<DamageDealt>()!;
        Assert.True(hit.PrintsOneHitKoLine);
        Assert.False(hit.PrintsCritLine);
        Assert.Equal(HitLineWording.Short, hit.Wording);
    }

    [Fact]
    public async Task ALandedOhkoReportsTheMovesRealEffectiveness()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Defender(TestCreatures.Make("D", type1: DamageType.Rock, hp: 500))
            .Use(Move("fissure"));

        Assert.Equal(2.0, result.First<DamageDealt>()!.TypeEffectiveness);
    }

    [Fact]
    public async Task AnOhkoThatBreaksASubstituteReportsItsLinesAfterTheBreak()
    {
        var foe = TestCreatures.Make("D", type1: DamageType.Rock, hp: 500);
        foe.Battle.SubstituteHp = 51;

        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Defender(foe)
            .Use(Move("fissure"));

        var events = result.Events.ToList();
        int broke = events.FindIndex(e => e is SubstituteBroke);
        var report = Assert.Single(result.Emitter.Of<SoakedHitReport>());
        Assert.True(broke >= 0);
        Assert.True(events.IndexOf(report) > broke, "the lines follow the break");
        Assert.True(report.PrintsOneHitKoLine);
        Assert.False(report.PrintsCritLine);
        Assert.Equal(2.0, report.TypeEffectiveness);
    }

    [Fact]
    public async Task AFailedOhkoPrintsNoLine()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", speed: 50))
            .Defender(TestCreatures.Make("D", hp: 500, speed: 200))
            .Use(Move("guillotine"));

        Assert.DoesNotContain(result.Hits, h => h.PrintsOneHitKoLine);
        Assert.False(result.Has<SoakedHitReport>());
    }

    [Theory]
    [InlineData("guillotine")]
    [InlineData("horn-drill")]
    [InlineData("fissure")]
    public async Task FailsWhenTargetOutspeedsUser(string moveName)
    {
        // Gen 1: the OHKO fails outright when the target is faster than the user — a Speed
        // comparison, regardless of level (a higher-level user can't OHKO a faster target).
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50, speed: 50))
            .Defender(TestCreatures.Make("D", level: 50, hp: 500, speed: 200))
            .Use(Move(moveName));

        Assert.Equal(UnaffectedCause.OneHitKo, result.First<MoveUnaffected>()?.Cause);
        Assert.False(result.Has<MoveMissed>()); // Gen 1: a failed OHKO is not a miss
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }

    [Theory]
    [InlineData("guillotine")]
    [InlineData("horn-drill")]
    [InlineData("fissure")]
    public async Task SucceedsWhenUserOutspeedsTargetEvenAtLowerLevel(string moveName)
    {
        // The Gen 1 check is Speed only: a lower-level but faster user still lands the OHKO.
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 40, speed: 200))
            .Defender(TestCreatures.Make("D", level: 60, hp: 500, speed: 50))
            .Use(Move(moveName));

        Assert.Equal(500, result.TotalDamage);
        Assert.False(result.Defender.IsAlive());
    }

    [Theory]
    [InlineData("guillotine")]
    [InlineData("horn-drill")]
    [InlineData("fissure")]
    public async Task MissesOnAccuracyFail(string moveName)
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", level: 50, hp: 500))
            .Rules(NeverHitRules.Instance)
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }
}
