using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// The default enemy brain: an intelligent-but-fallible Gen 1 move selector. It scores every candidate with
/// a <see cref="CompositeEvaluator"/> (damage-led, with the authentic Gen 1 leanings toward super-effective
/// hits, useful setup, and fresh status), then picks <i>probabilistically</i> via a softmax over those
/// scores rather than always taking the best move.
///
/// <para>That probabilistic pick is the deliberate design point the brief asked for: "more intelligent, but
/// keep some Gen 1 quirks / bad decision-making." It usually plays the strong move, yet — like a real RBY
/// trainer — sometimes throws out a worse one, and it never plans beyond the current turn. The
/// <paramref name="intelligence"/> knob (0 = nearly random, 1 = nearly greedy) maps to the softmax
/// temperature, so different trainer tiers (wild < trainer < gym leader) are one number, not new code. The
/// Gen 1 "quirks" themselves — discourage a redundant status, encourage type advantage — live in the
/// evaluators, so this class stays a thin, generation-blind selection policy hosted behind
/// <see cref="IBattleAi"/>.</para>
/// </summary>
public sealed class Gen1TrainerAi : IBattleAi
{
    private readonly IMoveEvaluator _evaluator;
    private readonly IRandomSource _rng;
    private readonly double _temperature;

    /// <param name="evaluator">Scoring personality; defaults to <see cref="CompositeEvaluator.CreateDefault"/>.</param>
    /// <param name="rng">Randomness seam, so a brain's choices replay under a seed; defaults to the system source.</param>
    /// <param name="intelligence">0 → nearly uniform (dumb), 1 → nearly greedy (always best). Default 0.7 is
    /// "mostly plays the strong move, occasionally errs."</param>
    public Gen1TrainerAi(
        IMoveEvaluator? evaluator = null,
        IRandomSource? rng = null,
        double intelligence = 0.7
    )
    {
        _evaluator = evaluator ?? CompositeEvaluator.CreateDefault();
        _rng = rng ?? SystemRandomSource.Instance;
        // Higher intelligence ⇒ lower temperature ⇒ sharper preference for the best-scoring move. Floored so a
        // fully "smart" brain still isn't a perfect optimiser (keeps a little Gen 1 fallibility).
        _temperature = 0.15 + (1.0 - Math.Clamp(intelligence, 0.0, 1.0)) * 0.85;
    }

    public PokemonAttack ChooseMove(IReadOnlyList<PokemonAttack> candidates, TurnContext context)
    {
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "Gen1TrainerAi.ChooseMove called with no candidates. "
                    + "Battle should have bypassed the AI and passed null (Struggle) directly."
            );
        if (candidates.Count == 1)
            return candidates[0];

        var scores = new double[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
            scores[i] = _evaluator.Score(candidates[i], context);

        // Softmax weights: exp(score / temperature), shifted by the max score first for numerical stability
        // (the shift cancels out across the ratio, so it doesn't change the distribution).
        double max = scores.Max();
        var weights = new double[scores.Length];
        double total = 0;
        for (int i = 0; i < scores.Length; i++)
        {
            weights[i] = Math.Exp((scores[i] - max) / _temperature);
            total += weights[i];
        }

        double roll = _rng.NextDouble() * total;
        double cumulative = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return candidates[i];
        }

        return candidates[^1]; // floating-point guard — the cumulative sum can fall a hair short of total
    }
}
