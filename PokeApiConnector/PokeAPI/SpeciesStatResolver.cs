namespace PokeApiConnector.PokeAPI;

/// <summary>
/// Resolves a species' base stats <i>as of a generation</i> from PokeAPI's historical <c>past_stats</c> — the stat
/// analogue of <c>past_types</c> for typing (DATA_IMPORT.md §4.2). The importer stores the target generation's
/// values, so the engine never needs to know PokeAPI shows the modern ones (ARCHITECTURE.md §2.1).
/// <para>
/// A <c>past_stats</c> entry tagged generation X lists the stats that applied up to and including X, and only the
/// ones that later changed. To reach generation G, start from the current stats and lay entries tagged ≥ G over
/// them from the newest to the oldest, so the entry closest to G wins per stat. Gen 1's single Special is the
/// <c>special</c> stat of the <c>generation-i</c> entry.
/// </para>
/// Parameterised by <paramref name="generation"/> on purpose: nothing here says "Gen 1" except the caller, and a test
/// pins that a different generation resolves differently.
/// </summary>
public static class SpeciesStatResolver
{
    private static readonly Dictionary<string, int> GenerationOrdinal = new()
    {
        ["generation-i"] = 1,
        ["generation-ii"] = 2,
        ["generation-iii"] = 3,
        ["generation-iv"] = 4,
        ["generation-v"] = 5,
        ["generation-vi"] = 6,
        ["generation-vii"] = 7,
        ["generation-viii"] = 8,
        ["generation-ix"] = 9,
    };

    /// <summary>Base stats keyed by PokeAPI stat name (<c>hp</c>, <c>attack</c>, <c>defense</c>, <c>speed</c>,
    /// <c>special-attack</c>, <c>special-defense</c>, and <c>special</c> where Gen 1 had it).</summary>
    public static Dictionary<string, int> BaseStatsAsOf(PokeApiPokemon pokeData, int generation)
    {
        var stats = new Dictionary<string, int>();
        foreach (var s in pokeData.Stats ?? [])
            if (s.Stat?.Name is { } name)
                stats[name] = s.BaseStat;

        var applicable = (pokeData.PastStats ?? [])
            .Select(e => (Entry: e, Ordinal: OrdinalOf(e.Generation?.Name, pokeData.Name)))
            .Where(x => x.Ordinal >= generation)
            .OrderByDescending(x => x.Ordinal);

        foreach (var (entry, _) in applicable)
        foreach (var s in entry.Stats ?? [])
            if (s.Stat?.Name is { } name)
                stats[name] = s.BaseStat;

        return stats;
    }

    // An entry whose generation we can't place can't be layered correctly — dropping it would quietly import the
    // wrong stats, so it is an import failure (the same stance as the rest of the importer).
    private static int OrdinalOf(string? generationName, string? species) =>
        generationName is not null && GenerationOrdinal.TryGetValue(generationName, out var ordinal)
            ? ordinal
            : throw new InvalidOperationException(
                $"{species}: unrecognised past_stats generation '{generationName}'."
            );
}
