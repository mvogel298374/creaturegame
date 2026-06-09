using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// The endless-battle-chain orchestrator: one persistent player runs encounter after encounter (enemies
/// supplied by a delegate so no DB is involved here) until it faints, then a single RunEnded carries the
/// summary. Verifies the win-counting, the per-encounter BattleStarted stream, cross-encounter XP
/// accumulation, and that RunEnded fires exactly once with the right totals.
/// </summary>
public class BattleRunnerTests
{
    [Fact]
    public async Task Runner_ChainsWins_ThenEmitsRunEndedWithSummary_WhenPlayerFaints()
    {
        const int targetWins = 3;
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
        int startXp = player.Experience;

        // The first `targetWins` enemies are slow 1-HP pushovers (player strikes first, one-shots, takes no
        // damage); the next is a fast bruiser that one-shots the player and ends the run.
        int built = 0;
        Func<Creature, Task<Creature>> supplier = _ =>
        {
            built++;
            var enemy =
                built <= targetWins
                    ? Fighter($"Pushover{built}", hp: 1, attack: 5, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50; // small award at L50 — XP grows, no level-up
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new BattleRunner(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0)
        );

        await runner.RunAsync();

        // Exactly one RunEnded, after the player has fainted, with the correct summary.
        var runEnded = Assert.Single(recorder.Of<RunEnded>());
        Assert.Equal(targetWins, runEnded.BattlesWon);
        Assert.Equal("Player", runEnded.FinalCreatureName);
        Assert.Equal(player.Level, runEnded.FinalLevel);
        Assert.False(player.IsAlive());

        // One encounter started per win plus the fatal one.
        Assert.Equal(targetWins + 1, recorder.Of<BattleStarted>().Count());
        Assert.Equal(targetWins + 1, built);

        // XP accumulated across the wins (no level-up at L50, so just the running total).
        Assert.True(player.Experience > startXp, "XP accumulates across chained encounters");
    }

    // Load-bearing for the client: RunEnded drives the game-over screen, so it must fire ONLY on a real
    // faint — never when the run is abandoned (client disconnect cancels the input mid-fight). A cancelled
    // input throws out of RunAsync before the emit, so no RunEnded should be recorded.
    [Fact]
    public async Task Runner_DoesNotEmitRunEnded_WhenInputIsCancelledMidRun()
    {
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
        var enemy = Fighter("Enemy", hp: 999, attack: 1, speed: 1, level: 50); // survives, so a turn is needed

        var recorder = new RecordingEmitter();

        var runner = new BattleRunner(
            player,
            _ => Task.FromResult(enemy),
            Gen1TypeChart.Instance,
            new CancelledInput(), // simulates the client having dropped — input throws on first choice
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0)
        );

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync());
        Assert.Empty(recorder.Of<RunEnded>());
    }

    // An input that always cancels — stands in for a disconnected client (mirrors SignalRInput.Cancel()).
    private sealed class CancelledInput : IBattleInput
    {
        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");
    }

    private static Creature Fighter(string name, int hp, int attack, int speed, int level)
    {
        var c = new Creature(name)
        {
            Level = level,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(level);
        c.Attributes.MaxHP = hp;
        c.Attributes.HP = hp;
        c.Attributes.Attack = attack;
        c.Attributes.Speed = speed;
        c.AddAttack(
            new Attack
            {
                Name = "tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
                PowerPointsMax = 99,
            }
        );
        return c;
    }
}
