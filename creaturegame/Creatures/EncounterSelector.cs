using creaturegame.DB;

namespace creaturegame.Creatures;

public static class EncounterSelector
{
    public static int Bst(PokemonSpecies s) =>
        s.BaseHP + s.BaseAttack + s.BaseDefense + s.BaseSpecial + s.BaseSpeed;

    /// <summary>
    /// Picks a random species from <paramref name="pool"/> whose BST falls within
    /// ±15 % of <paramref name="playerBst"/>. Automatically widens to ±25 %, ±50 %,
    /// then the full pool if no candidates are found at a tighter window.
    /// </summary>
    public static PokemonSpecies? PickByBst(List<PokemonSpecies> pool, int playerBst)
    {
        foreach (var pct in new[] { 0.15, 0.25, 0.50, 1.0 })
        {
            int lo = (int)(playerBst * (1.0 - pct));
            int hi = (int)(playerBst * (1.0 + pct));
            var candidates = pool.Where(s => Bst(s) >= lo && Bst(s) <= hi).ToList();
            if (candidates.Count > 0)
                return candidates[Random.Shared.Next(candidates.Count)];
        }
        return pool.Count > 0 ? pool[Random.Shared.Next(pool.Count)] : null;
    }
}
