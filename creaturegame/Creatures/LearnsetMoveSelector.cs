using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.DB;

namespace creaturegame.Creatures;

public enum MoveSelectionStrategy
{
    /// <summary>The 4 most-recently-learnable moves at the level — deterministic. Used for the player.</summary>
    CanonicalLatest,

    /// <summary>Weighted-random pick biased toward strong same-type attacks — varied. Used for enemies.</summary>
    WeightedSmart,
}

/// <summary>
/// Picks a creature's starting moveset from its learnset. Generation-agnostic: it consumes
/// learnset rows that the caller has already filtered to the active generation, so there is
/// no generation branching here.
/// <para>
/// RNG is taken through <see cref="IRandomSource"/> (defaulting to the shared source), the
/// same seam used across the engine — a <see cref="SeededRandomSource"/> makes
/// <see cref="MoveSelectionStrategy.WeightedSmart"/> reproducible in tests.
/// </para>
/// <para>
/// The <see cref="MoveSelectionStrategy.WeightedSmart"/> heuristic is a deliberate precursor
/// to the planned <c>IMoveEvaluator</c> / <c>WeightedAIInput</c> (see TODO "AI Move
/// Selection"); the weighting can later be refactored to share that evaluator.
/// </para>
/// </summary>
public static class LearnsetMoveSelector
{
    private const int MaxMoves = 4;

    // Flat weights so non-power moves still compete without dominating.
    private const double FixedDamageScore = 60.0;  // OHKO/Fixed/SuperFang/etc. — attractive, not infinite
    private const double StatusMoveScore  = 35.0;  // status/stat moves — present sometimes
    private const double StabMultiplier   = 1.5;

    public static IReadOnlyList<Attack> Select(
        MoveSelectionStrategy strategy,
        IReadOnlyList<PokemonLearnset> learnset,
        IReadOnlyDictionary<int, Attack> movesById,
        int level,
        DamageType type1,
        DamageType? type2,
        IRandomSource? rng = null)
    {
        // Candidate pool: learnable at/below this level, resolved to a known move, highest
        // learn level first (ties by move id) for a stable order.
        var candidates = learnset
            .Where(l => l.LearnLevel <= level && movesById.ContainsKey(l.MoveId))
            .OrderByDescending(l => l.LearnLevel)
            .ThenBy(l => l.MoveId)
            .Select(l => new Candidate(movesById[l.MoveId], l.LearnLevel))
            .ToList();

        if (candidates.Count <= MaxMoves)
            return candidates.OrderBy(c => c.LearnLevel).Select(c => c.Move).ToList();

        return strategy == MoveSelectionStrategy.CanonicalLatest
            ? SelectCanonicalLatest(candidates)
            : SelectWeightedSmart(candidates, type1, type2, rng ?? SystemRandomSource.Instance);
    }

    // The 4 moves with the highest learn level (candidates are already sorted that way).
    private static IReadOnlyList<Attack> SelectCanonicalLatest(List<Candidate> candidates) =>
        candidates.Take(MaxMoves).OrderBy(c => c.LearnLevel).Select(c => c.Move).ToList();

    private static IReadOnlyList<Attack> SelectWeightedSmart(
        List<Candidate> candidates, DamageType type1, DamageType? type2, IRandomSource rng)
    {
        var pool   = candidates.Select(c => new Weighted(c, Weight(c, type1, type2))).ToList();
        var chosen = new List<Candidate>(MaxMoves);

        // Guarantee at least one attack: take the highest-weight damaging move deterministically.
        var bestAttack = pool.Where(w => IsDamaging(w.Candidate.Move))
                             .OrderByDescending(w => w.Weight)
                             .FirstOrDefault();
        if (bestAttack is not null)
        {
            chosen.Add(bestAttack.Candidate);
            pool.Remove(bestAttack);
        }

        // Fill the rest by weighted draw without replacement, so same-level enemies vary.
        while (chosen.Count < MaxMoves && pool.Count > 0)
        {
            var pick = WeightedDraw(pool, rng);
            chosen.Add(pick.Candidate);
            pool.Remove(pick);
        }

        return chosen.OrderBy(c => c.LearnLevel).Select(c => c.Move).ToList();
    }

    private static Weighted WeightedDraw(List<Weighted> pool, IRandomSource rng)
    {
        double total = pool.Sum(w => w.Weight);
        double roll  = rng.NextDouble() * total;
        double acc   = 0;
        foreach (var w in pool)
        {
            acc += w.Weight;
            if (roll < acc) return w;
        }
        return pool[^1];  // floating-point guard — roll == total
    }

    private static double Weight(Candidate c, DamageType type1, DamageType? type2)
    {
        var move = c.Move;
        double score;
        if (IsDamaging(move))
        {
            score = move.DamageCategory == DamageCategory.Standard
                ? Math.Max(1, move.BaseDamage)   // power-based
                : FixedDamageScore;              // Fixed/LevelBased/OHKO/SuperFang/SelfDestruct/Drain
            if (move.DamageType == type1 || (type2.HasValue && move.DamageType == type2.Value))
                score *= StabMultiplier;         // STAB only matters for damaging moves
        }
        else
        {
            score = StatusMoveScore;
        }

        // Recency nudge: later-learned moves are usually upgrades, so gently favored.
        return score * (1 + c.LearnLevel / 100.0);
    }

    // Damaging = deals HP: a power move, or a damage category that ignores base power.
    private static bool IsDamaging(Attack move) =>
        move.BaseDamage > 0 || move.DamageCategory != DamageCategory.Standard;

    private sealed record Candidate(Attack Move, int LearnLevel);
    private sealed record Weighted(Candidate Candidate, double Weight);
}
