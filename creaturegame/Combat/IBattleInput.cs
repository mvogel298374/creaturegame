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
}

/// <summary>Context for a level-up move-replacement decision: who is learning, and the move on offer.</summary>
public sealed record MoveReplacementContext(Creature Learner, Attack NewMove);
