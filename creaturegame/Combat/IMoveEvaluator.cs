using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Scores a single candidate move's desirability in a given turn — the reusable building block an
/// <see cref="IBattleAi"/> brain composes to decide what to do. Higher score ⇒ more desirable; 0 means this
/// dimension is indifferent about the move (e.g. a damage evaluator scoring a pure status move); negative
/// means "actively avoid" (e.g. a no-effect type matchup).
///
/// <para>Evaluators are deliberately <b>generation-agnostic</b>: each measures one battle dimension
/// (<see cref="DamageEvaluator"/>, <see cref="TypeEffectivenessEvaluator"/>,
/// <see cref="StatStageMoveEvaluator"/>, <see cref="StatusMoveEvaluator"/>) using the seams already on
/// <see cref="TurnContext"/>. Combine them via <see cref="CompositeEvaluator"/> with per-personality weights.
/// What is generation/game-specific — which dimensions matter and how fallibly the result is acted on — lives
/// in the <see cref="IBattleAi"/> implementation (<see cref="Gen1TrainerAi"/>), not here.</para>
/// </summary>
public interface IMoveEvaluator
{
    double Score(PokemonAttack move, TurnContext context);
}
