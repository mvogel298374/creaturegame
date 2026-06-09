using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// Drives an endless chain of battles for one persistent player <see cref="Creature"/>: run a battle, and
/// while the player survives, build the next wild enemy and fight again. The player's permanent half (HP,
/// PP, XP, Level) carries across encounters; each <see cref="Battle"/> resets the transient half at its
/// start (canonical Gen 1). When the player faints the run ends and a single <see cref="RunEnded"/> is
/// emitted with the summary. The next enemy comes from <paramref name="enemySupplier"/>, so the data/DB
/// concern (building a scaled encounter) stays in the web layer and core stays generation-agnostic.
/// </summary>
public sealed class BattleRunner(
    Creature player,
    Func<Creature, Task<Creature>> enemySupplier,
    ITypeChart typeChart,
    IBattleInput playerInput,
    IBattleInput enemyInput,
    IReadOnlyList<Attack> movePool,
    IBattleEventEmitter? emitter = null,
    IBattleRules? rules = null,
    IRandomSource? rng = null
)
{
    public async Task RunAsync()
    {
        int battlesWon = 0;
        while (player.IsAlive())
        {
            var enemy = await enemySupplier(player);
            var battle = new Battle(
                player,
                enemy,
                typeChart,
                playerInput,
                enemyInput,
                movePool: movePool,
                rules: rules,
                emitter: emitter,
                rng: rng
            );
            await battle.StartFightAsync();

            // The battle ends when one side faints. If the player is still standing, the enemy dropped —
            // that's a win; otherwise the run is over.
            if (!player.IsAlive())
                break;
            battlesWon++;
        }

        emitter?.Emit(new RunEnded(battlesWon, player.Level, player.Name));
    }
}
