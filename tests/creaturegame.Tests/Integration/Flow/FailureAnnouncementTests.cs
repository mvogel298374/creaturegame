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
}
