namespace creaturegame.Generations;

/// <summary>
/// The generation registry — the one place that maps a <see cref="Generation"/> to its
/// <see cref="GenerationProfile"/>.
/// </summary>
/// <remarks>
/// <para>Deliberately the <i>only</i> legitimate switch on a generation in the whole codebase (alongside the
/// request parse at the web boundary). Everything downstream receives a profile and stays generation-blind —
/// <c>GENERATION_SEAMS.md §4.2</c>.</para>
/// <para>Mirrors <c>GameSessionManager.RunTuningByDifficulty</c> / <c>RunRulesFor</c>, the preset-lookup shape
/// already proven by the Difficulty feature (2026-07-22).</para>
/// <para><b>Adding a generation is one line here.</b> If adding one requires touching anything besides this
/// dictionary and the new profile file, a slice is missing from <see cref="GenerationProfile"/> — fix that
/// rather than special-casing.</para>
/// </remarks>
public static class GenerationProfiles
{
    private static readonly IReadOnlyDictionary<Generation, GenerationProfile> ByGeneration =
        new Dictionary<Generation, GenerationProfile> { [Generation.One] = Gen1Profile.Instance };

    // Materialised once — Registered sits on the request path (every /start and dex read parses against it),
    // and static field initializers run in textual order, so this must stay declared BELOW ByGeneration
    // (the same order trap Gen1Profile.Gen1Types documents).
    private static readonly IReadOnlyCollection<Generation> RegisteredGenerations =
        ByGeneration.Keys.ToArray();

    /// <summary>The profile for <paramref name="generation"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The generation has no registered profile. Deliberately a
    /// throw rather than a silent fall back to Gen 1: a missing profile is a wiring bug, and quietly serving
    /// Gen 1 is precisely the failure mode this feature exists to make impossible
    /// (<c>docs/GENERATION_PROFILE.md</c> §4.2). The web boundary handles untrusted input by parsing to a
    /// registered value <i>before</i> calling this.</exception>
    public static GenerationProfile For(Generation generation) =>
        ByGeneration.TryGetValue(generation, out var profile)
            ? profile
            : throw new ArgumentOutOfRangeException(
                nameof(generation),
                generation,
                $"No GenerationProfile is registered for {generation}."
            );

    /// <summary>Every registered generation, for tests and any UI that offers the choice.</summary>
    public static IReadOnlyCollection<Generation> Registered => RegisteredGenerations;
}
