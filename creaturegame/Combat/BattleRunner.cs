using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Evolution;

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
///
/// Evolution: after each win (and its level-ups) the optional <paramref name="checkEvolution"/> is consulted.
/// It is the data/DB seam for evolution — the web layer resolves the player's evolution edges through
/// <see cref="creaturegame.Evolution.IEvolutionRules"/> and, if one fires, returns the evolved species +
/// its learnset; the runner just applies it (mirrors <paramref name="enemySupplier"/>, keeping core
/// generation- and data-agnostic). Null (the default) means no evolution — the plain endless chain.
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
    int healEveryNBattles = 3,
    Func<Creature, Task<EvolutionOutcome?>>? checkEvolution = null
)
{
    public async Task RunAsync()
    {
        int battlesWon = 0;
        CarriedStatus? carried = null; // the player's major status, carried into the next encounter
        while (player.IsAlive())
        {
            var enemy = await enemySupplier(player);
            int levelBefore = player.Level;
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

            // Evolution check — Gen 1 attempts evolution on a level-up, so only when this battle actually
            // raised the player's level (a declined evolution then re-offers at the next level-up, not every
            // win). The battle has already applied the level-ups, so the level is current.
            if (player.Level > levelBefore)
                await TryEvolveAsync();

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

    // Offers, then applies, a pending evolution if the resolver reports one. The player can cancel (Gen 1
    // B-cancel) — the prompt blocks awaiting the decision; on cancel the creature is untouched and re-offered
    // at the next level-up. The from-identity is captured before EvolveTo (which overwrites name/species/stats)
    // so the events carry both forms for the sprite morph.
    private async Task TryEvolveAsync()
    {
        if (checkEvolution is null)
            return;
        if (await checkEvolution(player) is not { } evolution)
            return;

        string fromName = player.Name;
        int fromSpeciesId = player.SpeciesId;
        var newForm = evolution.NewForm;
        string toName = newForm.Name.ToUpper(); // matches how EvolveTo names the creature

        emitter?.Emit(new EvolutionOffered(fromName, toName, fromSpeciesId, newForm.Id));
        bool allow = await playerInput.ConfirmEvolutionAsync(
            new EvolutionPromptContext(player, newForm.Id, toName)
        );
        if (!allow)
        {
            emitter?.Emit(new EvolutionCancelled(fromName));
            return;
        }

        player.EvolveTo(newForm);
        player.Learnset = evolution.NewLearnset;

        emitter?.Emit(new CreatureEvolved(fromName, player.Name, fromSpeciesId, player.SpeciesId));

        // Evolution grants no moves itself, but the evolved form may learn one at the current level.
        await MoveLearning.LearnMovesForLevelAsync(player, player.Level, emitter, playerInput);
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
