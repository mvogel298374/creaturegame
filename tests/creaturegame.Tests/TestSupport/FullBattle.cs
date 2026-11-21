using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;

namespace creaturegame.Tests.TestSupport;

/// <summary>Runs one full scripted <see cref="Battle"/> to its end and returns every event it emitted.</summary>
public static class FullBattle
{
    public static async Task<RecordingEmitter> Fight(
        Creature player,
        Creature enemy,
        ScriptedInput playerInput,
        ScriptedInput enemyInput,
        IBattleRules? rules = null,
        IRandomSource? rng = null,
        IReadOnlyList<Attack>? movePool = null
    )
    {
        var emitter = new RecordingEmitter();
        await new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            playerInput,
            enemyInput,
            rules: rules ?? new ScriptableRules().Deterministic(),
            movePool: movePool,
            emitter: emitter,
            rng: rng ?? new SeededRandomSource(1)
        ).StartFightAsync();
        return emitter;
    }
}
