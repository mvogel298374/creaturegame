using creaturegame.Attacks;

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
}
