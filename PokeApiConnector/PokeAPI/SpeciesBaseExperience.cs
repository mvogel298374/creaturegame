using PokeApiConnector.Generation_1;

namespace PokeApiConnector.PokeAPI;

/// <summary>
/// The per-generation source for a species' <c>BaseExperience</c>. PokeAPI only serves today's value (rebalanced in
/// Gen 5) and keeps no history of it, so each supported generation brings a curated table. A generation without one
/// <b>throws</b> rather than quietly importing the modern value as if it were that generation's — the same
/// "never silently fall back to Gen 1/modern" stance as the engine's generation seams (GENERATION_SEAMS.md §5.0).
/// To add a generation: curate its table next to <see cref="Gen1BaseExperience"/> and add a case here.
/// </summary>
public static class SpeciesBaseExperience
{
    public static int For(int generation, int speciesId) =>
        generation switch
        {
            1 when speciesId > 0 && speciesId < Gen1BaseExperience.ByDexId.Length =>
                Gen1BaseExperience.ByDexId[speciesId],
            1 => throw new ArgumentOutOfRangeException(
                nameof(speciesId),
                $"Species {speciesId} is outside the Gen 1 dex (1-{Gen1BaseExperience.ByDexId.Length - 1})."
            ),
            _ => throw new NotSupportedException(
                $"No curated base-experience table for generation {generation}; PokeAPI has no history for it."
            ),
        };
}
