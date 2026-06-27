using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.DB;

namespace creaturegame.Creatures;

public enum MoveSelectionStrategy
{
    /// <summary>The 4 most-recently-learnable moves at the level — deterministic. Used for the player.</summary>
    CanonicalLatest,

    /// <summary>Weighted-random pick biased toward strong same-type attacks — varied. The base enemy strategy.</summary>
    WeightedSmart,

    /// <summary>
    /// The strongest <em>species-legal</em> moves (level-up + TM/HM), ignoring learn level — a foe that has
    /// been "taught" its best legal options. Drawn from the learnset rows the caller supplies (which must
    /// include Machine rows); deterministic top-N by score. The Strong tier.
    /// </summary>
    TmEnhanced,

    /// <summary>
    /// The strongest moves for the creature's types from the <em>entire</em> move pool, ignoring legality and
    /// level — a min-maxed, boss-grade set. Deterministic top-N by score. The Boss tier.
    /// </summary>
    Optimal,
}

/// <summary>
/// Picks a creature's starting moveset from its learnset (or, for the strong tiers, a wider pool).
/// Generation-agnostic: it consumes learnset rows that the caller has already filtered to the active
/// generation and learn method, so there is no generation branching here.
/// <para>
/// RNG is taken through <see cref="IRandomSource"/> (defaulting to the shared source), the same seam used
/// across the engine — a <see cref="SeededRandomSource"/> makes <see cref="MoveSelectionStrategy.WeightedSmart"/>
/// reproducible in tests. <see cref="MoveSelectionStrategy.TmEnhanced"/> / <see cref="MoveSelectionStrategy.Optimal"/>
/// are deterministic (top-N by score), so they draw no RNG at all.
/// </para>
/// <para>
/// The <see cref="MoveSelectionStrategy.WeightedSmart"/> heuristic is a deliberate precursor to the planned
/// <c>IMoveEvaluator</c> / <c>WeightedAIInput</c> (see TODO "AI Move Selection"); the weighting can later be
/// refactored to share that evaluator.
/// </para>
/// </summary>
public static class LearnsetMoveSelector
{
    public const int MaxMoves = 4;

    // Flat weights so non-power moves still compete without dominating.
    private const double FixedDamageScore = 60.0; // OHKO/Fixed/SuperFang/etc. — attractive, not infinite
    private const double StatusMoveScore = 35.0; // status/stat moves — present sometimes

    // A selection-time heuristic weight only — NOT the battle STAB multiplier (that lives on
    // IBattleRules.StabMultiplier). This just nudges the enemy toward same-type attacks.
    private const double StabWeightBonus = 1.5;

    public static IReadOnlyList<Attack> Select(
        MoveSelectionStrategy strategy,
        IReadOnlyList<PokemonLearnset> learnset,
        IReadOnlyDictionary<int, Attack> movesById,
        int level,
        DamageType type1,
        DamageType? type2,
        IRandomSource? rng = null,
        int maxMoves = MaxMoves
    )
    {
        // The strong tiers ignore learn level and pick the best moves by score, deterministically.
        if (strategy == MoveSelectionStrategy.Optimal)
            return SelectBest(movesById.Values, type1, type2, maxMoves); // any move

        if (strategy == MoveSelectionStrategy.TmEnhanced)
        {
            // Species-legal pool: every learnset row the caller supplied (level-up + TM/HM), resolved to a move.
            var legal = learnset
                .Where(l => movesById.ContainsKey(l.MoveId))
                .Select(l => movesById[l.MoveId]);
            return SelectBest(legal, type1, type2, maxMoves);
        }

        // Base tiers: candidates learnable at/below this level, highest learn level first (ties by move id).
        var candidates = learnset
            .Where(l => l.LearnLevel <= level && movesById.ContainsKey(l.MoveId))
            .OrderByDescending(l => l.LearnLevel)
            .ThenBy(l => l.MoveId)
            .Select(l => new Candidate(movesById[l.MoveId], l.LearnLevel))
            .ToList();

        if (candidates.Count <= maxMoves)
            return candidates.OrderBy(c => c.LearnLevel).Select(c => c.Move).ToList();

        return strategy == MoveSelectionStrategy.CanonicalLatest
            ? SelectCanonicalLatest(candidates, maxMoves)
            : SelectWeightedSmart(
                candidates,
                type1,
                type2,
                rng ?? SystemRandomSource.Instance,
                maxMoves
            );
    }

    /// <summary>
    /// Composition-root entry point: resolves the move table from the full pool, runs
    /// <see cref="Select"/>, and — if the species has no usable learnset entries (e.g. the
    /// importer hasn't been run) — falls back to <paramref name="maxMoves"/> random moves so a creature is
    /// never shipped move-less. Keeps the "what moves does a creature get" policy in one place.
    /// </summary>
    public static IReadOnlyList<Attack> SelectWithFallback(
        MoveSelectionStrategy strategy,
        IReadOnlyList<PokemonLearnset> learnset,
        IReadOnlyList<Attack> allMoves,
        int level,
        DamageType type1,
        DamageType? type2,
        IRandomSource? rng = null,
        int maxMoves = MaxMoves
    )
    {
        var source = rng ?? SystemRandomSource.Instance;
        var movesById = allMoves.ToDictionary(m => m.Id);
        var moves = Select(strategy, learnset, movesById, level, type1, type2, source, maxMoves);
        if (moves.Count > 0)
            return moves;

        return allMoves.OrderBy(_ => source.Next(int.MaxValue)).Take(maxMoves).ToList();
    }

    // The N moves with the highest learn level (candidates are already sorted that way).
    private static IReadOnlyList<Attack> SelectCanonicalLatest(
        List<Candidate> candidates,
        int maxMoves
    ) => candidates.Take(maxMoves).OrderBy(c => c.LearnLevel).Select(c => c.Move).ToList();

    // The strongest N moves for the creature's types, deterministic (ties broken by move id). No RNG, no level
    // gate — used by the TmEnhanced/Optimal tiers over their respective (legal / full) pools.
    private static IReadOnlyList<Attack> SelectBest(
        IEnumerable<Attack> moves,
        DamageType type1,
        DamageType? type2,
        int maxMoves
    ) =>
        moves
            .OrderByDescending(m => MoveScore(m, type1, type2))
            .ThenBy(m => m.Id)
            .Take(maxMoves)
            .ToList();

    private static IReadOnlyList<Attack> SelectWeightedSmart(
        List<Candidate> candidates,
        DamageType type1,
        DamageType? type2,
        IRandomSource rng,
        int maxMoves
    )
    {
        var pool = candidates.Select(c => new Weighted(c, Weight(c, type1, type2))).ToList();
        var chosen = new List<Candidate>(maxMoves);

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
        while (chosen.Count < maxMoves && pool.Count > 0)
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
        double roll = rng.NextDouble() * total;
        double acc = 0;
        foreach (var w in pool)
        {
            acc += w.Weight;
            if (roll < acc)
                return w;
        }
        return pool[^1]; // floating-point guard — roll == total
    }

    // Selection-time desirability of a move for a creature of these types: power (or a flat score for
    // fixed-damage / status moves) with a same-type nudge. Shared by the weighted and best-N selectors.
    private static double MoveScore(Attack move, DamageType type1, DamageType? type2)
    {
        if (!IsDamaging(move))
            return StatusMoveScore;

        double score =
            move.DamageCategory == DamageCategory.Standard
                ? Math.Max(1, move.BaseDamage) // power-based
                : FixedDamageScore; // Fixed/LevelBased/OHKO/SuperFang/SelfDestruct/Drain
        if (move.DamageType == type1 || (type2.HasValue && move.DamageType == type2.Value))
            score *= StabWeightBonus; // same-type nudge (damaging moves only)
        return score;
    }

    private static double Weight(Candidate c, DamageType type1, DamageType? type2) =>
        // Recency nudge: later-learned moves are usually upgrades, so gently favored.
        MoveScore(c.Move, type1, type2) * (1 + c.LearnLevel / 100.0);

    // Damaging = deals HP: a power move, or a damage category that ignores base power.
    private static bool IsDamaging(Attack move) =>
        move.BaseDamage > 0 || move.DamageCategory != DamageCategory.Standard;

    private sealed record Candidate(Attack Move, int LearnLevel);

    private sealed record Weighted(Candidate Candidate, double Weight);
}
