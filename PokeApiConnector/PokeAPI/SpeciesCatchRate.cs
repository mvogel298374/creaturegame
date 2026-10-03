using PokeApiConnector.Generation_1;

namespace PokeApiConnector.PokeAPI;

/// <summary>
/// The per-generation source for a species' <c>CatchRate</c>. Same story as <see cref="SpeciesBaseExperience"/>:
/// PokeAPI serves only today's capture rate and no history, so each supported generation brings a curated table, and
/// a generation without one throws instead of importing the modern value as that generation's.
/// </summary>
public static class SpeciesCatchRate
{
    public static int For(int generation, int speciesId) =>
        generation switch
        {
            1 when speciesId > 0 && speciesId < Gen1CatchRate.ByDexId.Length =>
                Gen1CatchRate.ByDexId[speciesId],
            1 => throw new ArgumentOutOfRangeException(
                nameof(speciesId),
                $"Species {speciesId} is outside the Gen 1 dex (1-{Gen1CatchRate.ByDexId.Length - 1})."
            ),
            _ => throw new NotSupportedException(
                $"No curated catch-rate table for generation {generation}; PokeAPI has no history for it."
            ),
        };
}
