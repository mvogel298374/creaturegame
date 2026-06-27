using creaturegame.Combat;
using creaturegame.DB;

namespace creaturegame.Creatures;

public static class EncounterSelector
{
    /// <summary>
    /// Base-stat total — sums the five Gen 1 base stats, where <c>BaseSpecial</c> is the single combined
    /// Special. This is a Gen 1 schema assumption: when Gen 2 splits Special into SpAtk/SpDef (see
    /// <c>TODO.md</c> → Multi-Generation), this sum must follow the schema change. It is not a battle-seam rule.
    /// </summary>
    public static int Bst(PokemonSpecies s) =>
        s.BaseHP + s.BaseAttack + s.BaseDefense + s.BaseSpecial + s.BaseSpeed;

    /// <summary>
    /// Picks a random species from <paramref name="pool"/> whose BST falls within ±15 % of
    /// <paramref name="targetBst"/>, widening to ±25 %, ±50 %, then ±100 % if a tighter window is empty.
    /// The target is normally the player's BST, but the run layer passes a depth-scaled / tier-shifted target
    /// (see <c>EncounterFactory.ScaleTargetBst</c>) to climb the curve.
    /// <para>When <paramref name="biome"/> is given, the pool is first filtered to that biome's theme
    /// (<see cref="BiomeDefinition.Contains"/>) and the theme is <em>never</em> broken: if no themed species
    /// falls in any band, it returns the nearest-BST themed species rather than an off-theme one. When the biome
    /// is null the legacy behaviour stands — the final fallback is a random member of the whole pool. Returns
    /// null only when the (filtered) pool is empty.</para>
    /// </summary>
    public static PokemonSpecies? PickByBst(
        List<PokemonSpecies> pool,
        int targetBst,
        IRandomSource? rng = null,
        BiomeDefinition? biome = null
    )
    {
        var r = rng ?? SystemRandomSource.Instance;
        var candidatePool = biome is null ? pool : pool.Where(biome.Contains).ToList();
        if (candidatePool.Count == 0)
            return null;

        foreach (var pct in new[] { 0.15, 0.25, 0.50, 1.0 })
        {
            int lo = (int)(targetBst * (1.0 - pct));
            int hi = (int)(targetBst * (1.0 + pct));
            var candidates = candidatePool.Where(s => Bst(s) >= lo && Bst(s) <= hi).ToList();
            if (candidates.Count > 0)
                return candidates[r.Next(candidates.Count)];
        }
        // Nothing in any band (BST beyond ±100 %). In a biome, stay on-theme via the nearest-BST species;
        // otherwise keep the legacy random-from-pool fallback.
        return biome is null
            ? candidatePool[r.Next(candidatePool.Count)]
            : candidatePool.MinBy(s => Math.Abs(Bst(s) - targetBst));
    }
}
