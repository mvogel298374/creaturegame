namespace creaturegame.Combat;

/// <summary>
/// Combines several <see cref="IMoveEvaluator"/>s into one score as a weighted sum — the AI's "personality":
/// the weights decide how much it cares about raw damage vs. type advantage vs. setup vs. status. A trainer
/// class is just a different weight vector over the same building blocks. <see cref="CreateDefault"/> is the
/// balanced mix used by <see cref="Gen1TrainerAi"/>: damage-led, with a real but secondary pull toward
/// super-effective hits, setup, and status.
/// </summary>
public sealed class CompositeEvaluator(
    IReadOnlyList<(IMoveEvaluator Evaluator, double Weight)> terms
) : IMoveEvaluator
{
    private readonly IReadOnlyList<(IMoveEvaluator Evaluator, double Weight)> _terms = terms;

    public double Score(Attacks.PokemonAttack move, TurnContext context) =>
        _terms.Sum(t => t.Evaluator.Score(move, context) * t.Weight);

    public static CompositeEvaluator CreateDefault() =>
        new([
            (new DamageEvaluator(), 1.0),
            (new TypeEffectivenessEvaluator(), 0.6),
            (new StatStageMoveEvaluator(), 0.5),
            (new StatusMoveEvaluator(), 0.6),
        ]);
}
