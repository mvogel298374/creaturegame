using creaturegame.DB;

namespace creaturegame.Evolution;

/// <summary>
/// The lowest level a species could legitimately be encountered at, derived from its evolution chain — a
/// wild/draft encounter must never hand out a post-evolution species below the level it takes to reach
/// that form (ENCOUNTER_DESIGN.md §3.8). Generation-agnostic: it only walks whatever <see cref="PokemonEvolution"/>
/// edges the caller supplies and asks the injected <see cref="IEvolutionRules"/> what floor each edge imposes
/// (<see cref="IEvolutionRules.MinLevelFor"/>) — the per-trigger interpretation (Level/Trade/Stone) lives on
/// that seam, never here, so a new generation needs only a new <see cref="IEvolutionRules"/>, not an edit to
/// this walk.
/// </summary>
public static class EvolutionMinLevel
{
    /// <summary>
    /// Walks <paramref name="speciesId"/>'s incoming edge back to its root, taking the highest floor any edge
    /// along the way imposes (each edge's floor already implies its predecessor's — see
    /// <see cref="IEvolutionRules.MinLevelFor"/> — so the chain's floor is a max, never a sum). Iterative with
    /// a visited-set guard: a self-edge or cycle in imported data ends the walk instead of overflowing the
    /// stack. A species with no incoming edge (a base form) floors at 0.
    /// </summary>
    public static int Compute(
        int speciesId,
        IReadOnlyList<PokemonEvolution> edges,
        IEvolutionRules rules
    )
    {
        int floor = 0;
        var visited = new HashSet<int> { speciesId };
        int current = speciesId;

        while (true)
        {
            var edge = edges.FirstOrDefault(e => e.ToSpeciesId == current);
            if (edge is null || !visited.Add(edge.FromSpeciesId))
                break;

            floor = Math.Max(floor, rules.MinLevelFor(edge));
            current = edge.FromSpeciesId;
        }

        return floor;
    }
}
