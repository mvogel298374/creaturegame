using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Scores a single move candidate for AI decision-making.
/// Higher scores indicate a more desirable move given the current TurnContext.
///
/// Implement one evaluator per concern (damage, type effectiveness, status value,
/// PP conservation) and combine them via a CompositeEvaluator with per-personality
/// weights. See Priority 9 in TODO.md for the full design.
/// </summary>
public interface IMoveEvaluator
{
    double Score(PokemonAttack move, TurnContext context);
}
