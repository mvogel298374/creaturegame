using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Gen 1's AI handicap (pokered <c>effects.asm</c> <c>StatModifierDownEffect</c>): the computer's stat-down moves fail
/// 64/256 of the time, before the Substitute check and, for a primary move, before accuracy; a failed secondary drop
/// is silent. The player never takes the roll. Rolls are forced through <see cref="ScriptableRules"/>.
/// </summary>
[Collection(MovesCollection.Name)]
public class StatDownHandicapContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    private static ScriptableRules Handicap(bool fails) =>
        new ScriptableRules().Deterministic().StatDownHandicap(fails);

    private Creature Mon(string name, int hp, int speed, params string[] moveNames)
    {
        var c = TestCreatures.Make(name, hp: hp, attack: 200, speed: speed);
        foreach (var n in moveNames)
            c.AddAttack(Move(n));
        return c;
    }

    private static bool Dropped(BattleScenarioResult result, string name) =>
        result.All<StatStageChanged>().Any(s => s.CreatureName == name && s.Delta < 0);

    [Fact]
    public async Task TheFoesGrowl_FailsOnTheRoll_WithButItFailed()
    {
        var result = await new BattleScenario()
            .Player(Mon("Player", 999, 200, "tackle"))
            .Enemy(Mon("Enemy", 100, 1, "growl"))
            .PlayerUses("tackle")
            .EnemyUses("growl")
            .Rules(Handicap(fails: true))
            .RunAsync();

        Assert.True(result.Has<MoveFailed>());
        Assert.False(result.Has<MoveMissed>());
        Assert.False(Dropped(result, "Player"));
    }

    [Fact]
    public async Task TheFoesGrowl_LandsWhenTheRollPasses()
    {
        var result = await new BattleScenario()
            .Player(Mon("Player", 999, 200, "tackle"))
            .Enemy(Mon("Enemy", 100, 1, "growl"))
            .PlayerUses("tackle")
            .EnemyUses("growl")
            .Rules(Handicap(fails: false))
            .RunAsync();

        Assert.False(result.Has<MoveFailed>());
        Assert.True(Dropped(result, "Player"));
    }

    [Fact]
    public async Task TheFoesAuroraBeam_StillHits_ButItsFailedDropIsSilent()
    {
        var result = await new BattleScenario()
            .Player(Mon("Player", 999, 200, "tackle"))
            .Enemy(Mon("Enemy", 100, 1, "aurora-beam"))
            .PlayerUses("tackle")
            .EnemyUses("aurora-beam")
            .Rules(Handicap(fails: true).ForceSecondary())
            .RunAsync();

        Assert.NotEmpty(result.DamageTo("Player"));
        Assert.False(Dropped(result, "Player"));
        Assert.False(result.Has<MoveFailed>());
    }

    // Behind a Substitute a passing roll is silently blocked, so "But it failed!" proves the roll came first.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task TheRoll_PrecedesTheSubstituteCheck(bool fails, bool expectFailedLine)
    {
        var result = await new BattleScenario()
            .Player(Mon("Player", 999, 200, "substitute", "tackle"))
            .Enemy(Mon("Enemy", 100, 1, "growl"))
            .PlayerUses("substitute", "tackle")
            .EnemyUses("growl")
            .Rules(Handicap(fails))
            .RunAsync();

        Assert.Equal(expectFailedLine, result.Has<MoveFailed>());
        Assert.False(Dropped(result, "Player"));
    }

    [Fact]
    public async Task ThePlayersStatDownMoves_NeverTakeTheRoll()
    {
        var rules = Handicap(fails: true).ForceSecondary();
        var result = await new BattleScenario()
            .Player(Mon("Player", 999, 200, "growl", "aurora-beam", "tackle"))
            .Enemy(Mon("Enemy", 150, 1, "splash"))
            .PlayerUses("growl", "aurora-beam", "tackle")
            .EnemyUses("splash")
            .Rules(rules)
            .RunAsync();

        Assert.False(result.Has<MoveFailed>());
        Assert.Equal(2, result.All<StatStageChanged>().Count(s => s.CreatureName == "Enemy"));
        Assert.Equal(0, rules.StatDownHandicapRolls);
    }

    [Fact]
    public async Task TheFoesMetronome_CallingGrowl_TakesTheRoll()
    {
        var result = await new MoveScenario()
            .Rules(Handicap(fails: true))
            .Side(BattleSide.Opponent)
            .MovePool(Move("metronome"), Move("growl"))
            .Use(Move("metronome"));

        Assert.Contains(result.Emitter.Of<MoveUsed>(), e => e.MoveName == "growl");
        Assert.True(result.Has<MoveFailed>());
        Assert.False(result.Has<StatStageChanged>());
    }

    [Fact]
    public async Task TheFoesMirrorMove_CopyingThePlayersGrowl_TakesTheRoll()
    {
        var result = await new BattleScenario()
            .Player(Mon("Player", 999, 200, "growl", "tackle"))
            .Enemy(Mon("Enemy", 100, 1, "mirror-move"))
            .PlayerUses("growl", "tackle")
            .EnemyUses("mirror-move")
            .Rules(Handicap(fails: true))
            .RunAsync();

        Assert.Contains(
            result.All<MoveUsed>(),
            e => e.AttackerName == "Enemy" && e.MoveName == "growl"
        );
        Assert.True(result.Has<MoveFailed>());
        Assert.False(Dropped(result, "Player"));
        Assert.True(Dropped(result, "Enemy"));
    }

    // pokered skips a side effect once the hit faints the target (stat-downs aren't in AlwaysHappenSideEffects).
    [Theory]
    [InlineData(1, 0)]
    [InlineData(999, 1)]
    public async Task AKnockOutSkipsTheSecondaryDrop_AndItsRoll(int defenderHp, int expectedRolls)
    {
        var rules = Handicap(fails: false).ForceSecondary();
        await new MoveScenario()
            .Rules(rules)
            .Side(BattleSide.Opponent)
            .Defender(TestCreatures.Make("Defender", hp: defenderHp))
            .Use(Move("aurora-beam"));

        Assert.Equal(expectedRolls, rules.StatDownHandicapRolls);
    }

    // On a 0 roll the foe's Growl fails, and its only draw is the handicap roll: no accuracy roll follows.
    [Fact]
    public async Task TheFoesFailedGrowl_DrawsNoAccuracyRoll()
    {
        var rng = new RecordingRandomSource(new LowestRollSource());
        var result = await new MoveScenario()
            .Rules(new Gen1BattleRules(rng))
            .Rng(rng)
            .Side(BattleSide.Opponent)
            .Use(Move("growl"));

        Assert.True(result.Has<MoveFailed>());
        Assert.Equal([RecordingRandomSource.Accuracy], rng.Draws); // Next(256): the handicap roll's bound too
    }

    [Fact]
    public async Task ThePlayersGrowl_OnTheSameRolls_Lands()
    {
        var rng = new RecordingRandomSource(new LowestRollSource());
        var result = await new MoveScenario()
            .Rules(new Gen1BattleRules(rng))
            .Rng(rng)
            .Use(Move("growl"));

        Assert.False(result.Has<MoveFailed>());
        Assert.True(result.Has<StatStageChanged>());
        Assert.Single(rng.Draws, d => d == RecordingRandomSource.Accuracy); // accuracy, no Next(256) handicap roll
    }
}
