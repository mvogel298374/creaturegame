namespace creaturegame.Generations;

/// <summary>
/// Which Pokémon generation a run is played under. Selected once at run start and never changed mid-run
/// (one generation per run — see <c>docs/GENERATION_PROFILE.md</c> §1 decision 4).
/// </summary>
/// <remarks>
/// <para><b>This enum names generations; it must never be branched on.</b> A generation difference is expressed
/// by <i>which <see cref="GenerationProfile"/> is selected</i>, never by an inspection inside game logic — the
/// invariant in <c>GENERATION_SEAMS.md §4.2</c>.</para>
/// <para>Legitimate reads are: the registry lookup in <see cref="GenerationProfiles"/>, the request parse at the
/// web boundary, and <b>using the value as a data filter</b> — e.g. selecting a generation's learnset or
/// evolution rows from the database (Stage 1b replaces <c>EncounterFactory.ActiveGeneration</c> with exactly
/// that). What is forbidden is branching on it to pick <i>behaviour</i>; asking the data layer for this
/// generation's rows is not a branch.</para>
/// <para><b>Adding a generation:</b> add a member here, add a <c>GenN Profile</c> file beside
/// <c>Gen1Profile.cs</c>, and add one line to <see cref="GenerationProfiles"/>. Nothing else should need to
/// change — if it does, that is the signal a slice is missing from <see cref="GenerationProfile"/>.</para>
/// </remarks>
public enum Generation
{
    /// <summary>Red/Blue/Yellow. The only generation currently implemented.</summary>
    One = 1,
}
