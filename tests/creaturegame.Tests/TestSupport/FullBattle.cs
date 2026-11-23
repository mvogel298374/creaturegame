using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Tests.TestSupport;

/// <summary>Runs one full scripted <see cref="Battle"/> to its end and returns every event it emitted.</summary>
public static class FullBattle
{
    public static async Task<RecordingEmitter> Fight(
        Creature player,
        Creature enemy,
        IBattleInput playerInput,
        IBattleInput enemyInput,
        IBattleRules? rules = null,
        IRandomSource? rng = null,
        IReadOnlyList<Attack>? movePool = null,
        Party? playerParty = null,
        Bag? playerBag = null
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
            rng: rng ?? new SeededRandomSource(1),
            playerBag: playerBag,
            playerParty: playerParty
        ).StartFightAsync();
        return emitter;
    }
}
