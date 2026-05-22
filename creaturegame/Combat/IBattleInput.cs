using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Abstracts move selection for one side of a battle.
/// Assign one implementation per side when constructing <see cref="Battle"/>.
///
/// Implementations: AutoSelectInput (current default), ConsoleInput (Priority 6),
/// RandomMoveInput / GreedyAIInput / WeightedAIInput (Priority 9).
///
/// This interface is only called when a real choice exists. Struggle is a
/// system-enforced fallback — Battle detects IsOutOfPP and bypasses IBattleInput,
/// passing null directly to AttackAction.
/// </summary>
public interface IBattleInput
{
    Task<PokemonAttack> ChooseMoveAsync(TurnContext context);
}
