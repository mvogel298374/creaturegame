using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.Integration.Interactions;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// End-to-end battle-flow probes driven through the <b>real, imported</b> moves (via
/// <see cref="BattleScenario"/> + the live moves DB) — the integrated counterpart to
/// <c>BattleIntegrationTests</c>, which runs synthetic moves. These assert the overall lifecycle the
/// frontend depends on: a well-formed event stream from start to finish, and the win → XP → level-up
/// chain that no single-action or synthetic-move test reaches.
/// </summary>
[Collection(MovesCollection.Name)]
public class FullBattleFlowTests(MovesFixture moves) : InteractionTest(moves)
{
    // The event stream must be well formed: BattleStarted first, exactly one BattleEnded last, the loser
    // faints exactly once, and every turn but the final (fainting) one is closed by a TurnEnded — Battle
    // breaks out before emitting TurnEnded on the turn someone drops.
    [Fact]
    public async Task RealMoveBattle_ProducesAWellFormedLifecycleEventStream()
    {
        var player = Mon("Player", hp: 300, attack: 100, speed: 200, DamageType.Normal, "tackle");
        var enemy = Mon("Enemy", hp: 300, attack: 40, speed: 1, DamageType.Normal, "tackle");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("tackle")
            .EnemyUses("tackle")
            .RunAsync();

        var events = result.Events;
        Assert.IsType<BattleStarted>(events[0]);
        Assert.IsType<BattleEnded>(events[^1]);
        Assert.Equal(1, result.Count<BattleEnded>());

        // The slower, weaker enemy is the one that drops, and it faints exactly once.
        Assert.Equal(1, result.Count<CreatureFainted>());
        Assert.Equal("Enemy", result.First<CreatureFainted>()!.Name);
        Assert.Equal("Player", result.Winner);
        Assert.True(player.IsAlive());
        Assert.False(enemy.IsAlive());

        // Turns pair up except the last: the fainting turn has a TurnStarted but no TurnEnded.
        int started = result.Count<TurnStarted>();
        int ended = result.Count<TurnEnded>();
        Assert.True(started >= 2, "expected a multi-turn fight");
        Assert.Equal(started - 1, ended);
    }

    // Beating an enemy awards Gen 1 wild XP (BaseExperience × level / 7); a big award on an underleveled
    // winner crosses several thresholds at once, and Battle must emit one LeveledUp per level crossed, in
    // a consecutive run — the sequence the XP bar and (future) move-learning prompt step through.
    [Fact]
    public async Task WinningBattle_AwardsXp_AndEmitsConsecutiveLeveledUpEvents()
    {
        var player = new Creature("Player")
        {
            Level = 5,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        player.CalculateStats();
        player.Experience = player.CalculateExperienceForLevel(5); // sit exactly at the level-5 floor
        player.Attributes.MaxHP = 999;
        player.Attributes.HP = 999;
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 200;
        player.AddAttack(Move("tackle"));

        // A high-level enemy with real base XP, made one-shottable so the player wins cleanly.
        var enemy = Mon("Enemy", hp: 20, attack: 5, speed: 1, DamageType.Normal, "splash");
        enemy.Level = 30;
        enemy.SpeciesBaseExperience = 200;

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("tackle")
            .EnemyUses("splash")
            .RunAsync();

        Assert.Equal("Player", result.Winner);

        var levels = result.All<LeveledUp>().Select(e => e.NewLevel).ToList();
        Assert.True(levels.Count >= 2, $"expected a multi-level XP award (got {levels.Count})");
        // One event per level crossed, consecutive, starting just above the player's old level.
        Assert.Equal(Enumerable.Range(6, levels.Count), levels);
        Assert.Equal(levels[^1], player.Level); // final event matches the creature's real level
    }
}
