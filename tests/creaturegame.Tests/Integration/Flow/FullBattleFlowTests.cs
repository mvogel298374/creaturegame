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

    // The win must emit exactly one ExperienceGained (carrying the Gen 1 wild award) and it must precede
    // every LeveledUp — the client needs the amount to start the bar fill before any level boundary plays.
    // Each LeveledUp then carries that level's bar parameters: XpThisLevel = total XP minus the level floor,
    // XpToNextLevel = the growth-rate span of that level.
    [Fact]
    public async Task WinningBattle_EmitsExperienceGained_BeforeLevelUps_AndLeveledUpCarriesCorrectThresholds()
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

        var enemy = Mon("Enemy", hp: 20, attack: 5, speed: 1, DamageType.Normal, "splash");
        enemy.Level = 30;
        enemy.SpeciesBaseExperience = 200;
        int expectedXp = (int)Math.Floor(200.0 * 30 / 7); // Gen 1 wild XP = BaseExp × level / 7

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("tackle")
            .EnemyUses("splash")
            .RunAsync();

        // Exactly one ExperienceGained, with the awarded amount, before the first LeveledUp.
        var gains = result.All<ExperienceGained>();
        Assert.Single(gains);
        Assert.Equal("Player", gains[0].CreatureName);
        Assert.Equal(expectedXp, gains[0].Amount);

        var events = result.Events;
        int xpIndex = IndexOfFirst<ExperienceGained>(events);
        int firstLevelIndex = IndexOfFirst<LeveledUp>(events);
        Assert.True(
            xpIndex >= 0 && firstLevelIndex > xpIndex,
            "ExperienceGained must be emitted before the first LeveledUp"
        );

        // Total XP doesn't change across the level-up loop, so each event's thresholds derive from the
        // growth-rate table at that level: XpThisLevel = Experience − floor(level), XpToNextLevel = span.
        foreach (var lu in result.All<LeveledUp>())
        {
            int floor = player.CalculateExperienceForLevel(lu.NewLevel);
            int span = player.CalculateExperienceForLevel(lu.NewLevel + 1) - floor;
            Assert.Equal(player.Experience - floor, lu.XpThisLevel);
            Assert.Equal(span, lu.XpToNextLevel);
        }

        // Bar-fill contract the client relies on: an intermediate level's XP overshoots its span (the
        // client caps the fill at full, then the next level resets the bar), while the final level is a
        // partial fill. Guards the Math.min(...) cap in timeline.ts from being silently removed.
        var levelUps = result.All<LeveledUp>();
        for (int i = 0; i < levelUps.Count - 1; i++)
            Assert.True(
                levelUps[i].XpThisLevel >= levelUps[i].XpToNextLevel,
                $"intermediate level {levelUps[i].NewLevel} should overshoot its span (client caps the bar)"
            );
        Assert.True(
            levelUps[^1].XpThisLevel <= levelUps[^1].XpToNextLevel,
            "the final level is a partial fill"
        );

        // The final event's stat block is the creature's current (post-level-up) stats.
        Assert.Equal(player.StatSnapshot(), levelUps[^1].Stats);
    }

    // The stat block on LeveledUp must equal what CalculateStats produces at the new level — verified
    // against an independently-built reference creature, so a regression in the snapshot is caught.
    [Fact]
    public async Task LeveledUp_StatBlock_MatchesCalculateStatsAtTheNewLevel()
    {
        var player = BuildStatTestCreature("Player", level: 10);
        player.Experience = player.CalculateExperienceForLevel(10);
        player.Attributes.MaxHP = 999;
        player.Attributes.HP = 999;
        player.Attributes.Attack = 999; // win cleanly; overwritten by CalculateStats on level-up
        player.Attributes.Speed = 200;
        player.AddAttack(Move("tackle"));

        var enemy = Mon("Enemy", hp: 1, attack: 1, speed: 1, DamageType.Normal, "splash");
        enemy.Level = 35;
        enemy.SpeciesBaseExperience = 100; // award = floor(100×35/7) = 500 → exactly one level (10→11)

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("tackle")
            .EnemyUses("splash")
            .RunAsync();

        var lu = Assert.Single(result.All<LeveledUp>());
        Assert.Equal(11, lu.NewLevel);

        var reference = BuildStatTestCreature("Ref", level: 11);
        reference.CalculateStats();
        Assert.Equal(reference.StatSnapshot(), lu.Stats);
    }

    // TurnStarted must carry the player's level-relative XP so the bar is correct on entry and every turn —
    // numerator = XP into the current level, denominator = the level's span (not the remaining amount).
    [Fact]
    public async Task TurnStarted_CarriesPlayerLevelRelativeXp()
    {
        var player = BuildStatTestCreature("Player", level: 7);
        int into = 42;
        player.Experience = player.CalculateExperienceForLevel(7) + into;
        player.Attributes.MaxHP = 999;
        player.Attributes.HP = 999;
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 200;
        player.AddAttack(Move("tackle"));

        var enemy = Mon("Enemy", hp: 30, attack: 1, speed: 1, DamageType.Normal, "splash");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("tackle")
            .EnemyUses("splash")
            .RunAsync();

        var firstTurn = result.All<TurnStarted>()[0]; // emitted before any XP is awarded
        int span = player.CalculateExperienceForLevel(8) - player.CalculateExperienceForLevel(7);
        Assert.Equal(into, firstTurn.PlayerXpThisLevel);
        Assert.Equal(span, firstTurn.PlayerXpToNextLevel);
    }

    // At the level cap the XP-bar helpers report a full bar (XpThisLevel == XpToNextLevel > 0) instead of a
    // span into a non-existent level 101 — the client neither divides by a meaningless denominator nor
    // shows an empty bar on a maxed creature.
    [Fact]
    public void XpBarHelpers_AtMaxLevel_ReportAFullBar()
    {
        var maxed = BuildStatTestCreature("Maxed", level: Creature.MaxLevel);
        maxed.Experience = maxed.CalculateExperienceForLevel(Creature.MaxLevel) + 5000; // overshoot the cap
        Assert.True(maxed.XpToNextLevel > 0); // no div-by-zero on the client
        Assert.Equal(maxed.XpToNextLevel, maxed.XpThisLevel); // full bar
    }

    private static Creature BuildStatTestCreature(string name, int level)
    {
        // Pin DVs (the constructor randomises them) so a creature built at level N matches one that
        // levelled up to N — otherwise the stat blocks diverge on DVs alone.
        var c = new Creature(name)
        {
            Level = level,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
            BaseHP = 60,
            BaseAttack = 80,
            BaseDefense = 50,
            BaseSpecial = 40,
            BaseSpeed = 70,
            DvHP = 9,
            DvAttack = 9,
            DvDefense = 9,
            DvSpecial = 9,
            DvSpeed = 9,
        };
        c.CalculateStats();
        return c;
    }

    private static int IndexOfFirst<T>(IReadOnlyList<BattleEvent> events)
        where T : BattleEvent
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i] is T)
                return i;
        return -1;
    }
}
