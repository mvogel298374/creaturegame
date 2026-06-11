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
///
/// Roguelite "Poké Center" recovery: after every <paramref name="healEveryNBattles"/>-th win the player is
/// fully restored (HP, PP, status) before the next encounter — a full heal is generation-invariant (every
/// gen's Center does the same), so this is run-layer logic, not a battle seam. Set the interval to 0 to
/// disable recovery entirely.
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
    IRandomSource? rng = null,
    int healEveryNBattles = 3
)
{
    public async Task RunAsync()
    {
        int battlesWon = 0;
        CarriedStatus? carried = null; // the player's major status, carried into the next encounter
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
                rng: rng,
                playerEntryStatus: carried
            );
            await battle.StartFightAsync();

            // The battle ends when one side faints. If the player is still standing, the enemy dropped —
            // that's a win; otherwise the run is over.
            if (!player.IsAlive())
                break;
            battlesWon++;

            // Poké Center pause: every Nth win, offer a full restore before the next foe. The prompt is its own
            // step in the game loop — it blocks awaiting the player's accept/skip choice (interactive input
            // only; automated inputs accept by default, so the chain still heals headless/in tests). Accepting
            // cures status, so nothing carries; declining leaves the player as they were (status carries).
            if (healEveryNBattles > 0 && battlesWon % healEveryNBattles == 0)
            {
                emitter?.Emit(new RecoveryOffered(player.Name, player.SpeciesId, battlesWon));
                bool accept = await playerInput.ConfirmRecoveryAsync(
                    new RecoveryContext(player, battlesWon)
                );
                if (accept)
                {
                    player.FullHeal();
                    carried = null;
                    emitter?.Emit(new PlayerRecovered(player.Name, player.Attributes.HP));
                }
                else
                {
                    carried = CaptureCarriedStatus(player);
                    emitter?.Emit(new RecoveryDeclined(player.Name));
                }
            }
            else
            {
                carried = CaptureCarriedStatus(player);
            }
        }

        emitter?.Emit(new RunEnded(battlesWon, player.Level, player.Name));
    }

    // Major status carries into the next encounter; the generation decides what each status becomes out of
    // battle (Gen 1 reverts Toxic to regular Poison) via IBattleRules.CarryStatusOutOfBattle. Volatile
    // conditions (confusion, stat stages, …) live only in BattleState and are dropped by the per-battle
    // reset — they are never captured here. The sleep counter carries so a sleeping creature keeps counting
    // down across the boundary.
    private CarriedStatus? CaptureCarriedStatus(Creature c)
    {
        var status = (rules ?? Gen1BattleRules.Instance).CarryStatusOutOfBattle(c.Battle.Status);
        if (status == StatusCondition.None)
            return null;
        int sleepTurns = status == StatusCondition.Sleep ? c.Battle.SleepTurns : 0;
        return new CarriedStatus(status, sleepTurns);
    }
}
