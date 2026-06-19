using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// Abstracts move selection for one side of a battle.
/// Assign one implementation per side when constructing <see cref="Battle"/>.
///
/// Implementations: AutoSelectInput (current default), ConsoleInput (Priority 7),
/// RandomMoveInput / GreedyAIInput / WeightedAIInput (Priority 10).
///
/// This interface is only called when a real choice exists. Struggle is a
/// system-enforced fallback — when the creature has no selectable move
/// (<see cref="Creatures.Creature.CanSelectAnyMove"/> is false: out of PP, or its only
/// move is Disabled), Battle bypasses IBattleInput and passes null directly to AttackAction.
/// </summary>
public interface IBattleInput
{
    Task<PokemonAttack> ChooseMoveAsync(TurnContext context);

    /// <summary>
    /// Asked when the creature levels into a new move but its four slots are full: return the slot index
    /// (0–3) to forget and replace, or <c>null</c> to decline (keep the current moveset). Only the player
    /// input is ever consulted. The default is to decline, so AI / automated inputs never block on a
    /// level-up prompt — only an interactive input (the web <c>SignalRInput</c>) overrides it.
    /// </summary>
    Task<int?> ChooseMoveToForgetAsync(MoveReplacementContext context) =>
        Task.FromResult<int?>(null);

    /// <summary>
    /// Asked between encounters when a roguelite "Poké Center" recovery is offered: return <c>true</c> to fully
    /// restore the creature (HP/PP/status), <c>false</c> to skip it. Only the interactive player input is ever
    /// consulted; the default accepts, so automated / AI inputs never block on the prompt and the chain still
    /// heals on schedule — only the web <see cref="SignalRInput"/> blocks awaiting a real choice.
    /// </summary>
    Task<bool> ConfirmRecoveryAsync(RecoveryContext context) => Task.FromResult(true);

    /// <summary>
    /// Asked between encounters when an evolution is offered (after a level-up crosses the threshold): return
    /// <c>true</c> to let it evolve, <c>false</c> to cancel (Gen 1 B-cancel — it re-offers at the next
    /// level-up). Only the interactive player input is ever consulted; the default allows it, so automated /
    /// AI inputs never block on the prompt and still evolve on schedule — only the web
    /// <see cref="SignalRInput"/> blocks awaiting a real choice.
    /// </summary>
    Task<bool> ConfirmEvolutionAsync(EvolutionPromptContext context) => Task.FromResult(true);
}

/// <summary>Context for a level-up move-replacement decision: who is learning, and the move on offer.</summary>
public sealed record MoveReplacementContext(Creature Learner, Attack NewMove);

/// <summary>Context for a between-encounter Poké Center recovery offer: the player creature and the running
/// win count (so an input could vary its answer by depth — the default just accepts).</summary>
public sealed record RecoveryContext(Creature Player, int BattlesWon);

/// <summary>Context for a between-encounter evolution offer: the player creature and the form it would become
/// (id + display name), so an input could decide based on it — the default just allows.</summary>
public sealed record EvolutionPromptContext(Creature Player, int ToSpeciesId, string ToName);
