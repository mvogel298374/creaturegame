using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.Integration.Interactions;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>The rules seam picks a precondition failure's line. See GENERATION_PROFILE.md §3.</summary>
[Collection(MovesCollection.Name)]
public class FailureAnnouncementTests(MovesFixture moves) : InteractionTest(moves)
{
    private static IBattleRules AltRules() =>
        TestAltProfile.Instance.BuildBattleRules(new SeededRandomSource(0));

    private Task<BattleScenarioResult> SlowerOhkoBattle(IBattleRules? rules)
    {
        var player = Mon(
            "Player",
            hp: 300,
            attack: 999,
            speed: 50,
            DamageType.Normal,
            "fissure",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 1, attack: 1, speed: 200, DamageType.Normal, "splash");
        var scenario = new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("fissure", "tackle")
            .EnemyUses("splash");
        return (rules is null ? scenario : scenario.Rules(rules)).RunAsync();
    }

    private Task<BattleScenarioResult> TrainerRoarBattle(IBattleRules? rules)
    {
        var player = Mon(
            "Player",
            hp: 300,
            attack: 999,
            speed: 200,
            DamageType.Normal,
            "roar",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 1, attack: 1, speed: 1, DamageType.Normal, "splash");
        var scenario = new BattleScenario()
            .Escapable(false)
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("roar", "tackle")
            .EnemyUses("splash");
        return (rules is null ? scenario : scenario.Rules(rules)).RunAsync();
    }

    [Fact]
    public async Task Gen1SlowerOhkoUserIsAnnouncedAsUnaffectedNotMissed()
    {
        var result = await SlowerOhkoBattle(rules: null);

        var unaffected = Assert.Single(result.All<MoveUnaffected>());
        Assert.Equal(UnaffectedCause.OneHitKo, unaffected.Cause);
        Assert.Equal("Enemy", unaffected.TargetName);
        Assert.False(result.Has<MoveMissed>());
        Assert.Equal("Player", result.Winner);
    }

    [Fact]
    public async Task Gen1TrainerBattleRoarIsAnnouncedAsUnaffected()
    {
        var result = await TrainerRoarBattle(rules: null);

        var unaffected = Assert.Single(result.All<MoveUnaffected>());
        Assert.Equal(UnaffectedCause.ForceFlee, unaffected.Cause);
        Assert.False(result.Has<MoveHadNoEffect>());
        Assert.Equal("Player", result.Winner);
    }

    [Fact]
    public async Task TheSeamPicksTheOhkoFailureLine()
    {
        var result = await SlowerOhkoBattle(AltRules());

        Assert.False(result.Has<MoveUnaffected>());
        Assert.Contains(result.All<MoveMissed>(), e => e.MoveName == "fissure");
    }

    [Fact]
    public async Task TheSeamPicksTheTrainerRoarFailureLine()
    {
        var result = await TrainerRoarBattle(AltRules());

        Assert.False(result.Has<MoveUnaffected>());
        Assert.Contains(result.All<MoveHadNoEffect>(), e => e.MoveName == "roar");
    }

    private Task<BattleScenarioResult> ThunderWaveBattle(DamageType foeType, IBattleRules rules)
    {
        var player = Mon(
            "Player",
            hp: 300,
            attack: 999,
            speed: 200,
            DamageType.Normal,
            "thunder-wave",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 100, attack: 1, speed: 1, foeType, "splash");
        return new BattleScenario()
            .Rules(rules)
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("thunder-wave", "thunder-wave", "tackle")
            .EnemyUses("splash")
            .RunAsync();
    }

    private Task<BattleScenarioResult> MissedSleepPowderBattle(IBattleRules rules)
    {
        var player = Mon(
            "Player",
            hp: 300,
            attack: 999,
            speed: 200,
            DamageType.Normal,
            "sleep-powder",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 100, attack: 1, speed: 1, DamageType.Water, "rock-throw");
        return new BattleScenario()
            .Rules(rules)
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("sleep-powder", "tackle")
            .EnemyUses("rock-throw")
            .RunAsync();
    }

    [Fact]
    public async Task Gen1StatusMoveOnAStatusedFoeSaysDidntAffect()
    {
        var result = await ThunderWaveBattle(DamageType.Water, AlwaysHitRules.Instance);

        Assert.Single(result.All<StatusApplied>(), s => s.Status == StatusCondition.Paralysis);
        Assert.Single(result.All<MoveDidntAffect>(), e => e.MoveName == "thunder-wave");
        Assert.False(result.Has<MoveHadNoEffect>());
        Assert.Equal("Player", result.Winner);
    }

    [Fact]
    public async Task Gen1TypeImmunityStillSaysDoesntAffect()
    {
        var result = await ThunderWaveBattle(DamageType.Ground, AlwaysHitRules.Instance);

        Assert.Equal(2, result.All<MoveHadNoEffect>().Count(e => e.MoveName == "thunder-wave"));
        Assert.False(result.Has<MoveDidntAffect>());
        Assert.False(result.Has<StatusApplied>());
    }

    [Fact]
    public async Task TheSeamPicksTheStatusNoOpLine()
    {
        var result = await ThunderWaveBattle(DamageType.Water, new AltStatusLinesRules());

        Assert.Single(result.All<MoveHadNoEffect>(), e => e.MoveName == "thunder-wave");
        Assert.False(result.Has<MoveDidntAffect>());
    }

    [Fact]
    public async Task Gen1MissedSleepPowderSaysDidntAffect_AMissedRockThrowStillMisses()
    {
        var result = await MissedSleepPowderBattle(new MissesBelowEightyRules());

        Assert.Single(result.All<MoveDidntAffect>(), e => e.MoveName == "sleep-powder");
        Assert.DoesNotContain(result.All<MoveMissed>(), e => e.MoveName == "sleep-powder");
        Assert.Contains(result.All<MoveMissed>(), e => e.MoveName == "rock-throw");
        Assert.False(result.Has<StatusApplied>());
        Assert.Equal("Player", result.Winner);
    }

    [Fact]
    public async Task TheSeamPicksTheStatusMissLine()
    {
        var result = await MissedSleepPowderBattle(new AltStatusLinesRules());

        Assert.Contains(result.All<MoveMissed>(), e => e.MoveName == "sleep-powder");
        Assert.False(result.Has<MoveDidntAffect>());
    }

    // Forced roll: moves under 80% accuracy (Sleep Powder 75, Rock Throw 65) miss; Tackle and Thunder Wave hit.
    private class MissesBelowEightyRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) =>
            acc < 80 ? 0 : 256;
    }

    private sealed class AltStatusLinesRules : MissesBelowEightyRules
    {
        public override MoveFailureAnnouncement StatusMoveNoOpAnnouncement =>
            AltRules().StatusMoveNoOpAnnouncement;

        public override MoveFailureAnnouncement StatusMoveMissAnnouncement =>
            AltRules().StatusMoveMissAnnouncement;
    }
}
