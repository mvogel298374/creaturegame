namespace PokeApiConnector.PokeAPI;

/// <summary>
/// The per-generation facts the species/learnset importer needs and PokeAPI cannot tell it: which version group's
/// learnset is that generation's, where its move-id and dex-id ranges end. A generation without an entry
/// <b>throws</b> — resolved before any network call — instead of quietly importing with Gen 1's numbers
/// (GENERATION_SEAMS.md §5.0: never fall back to Gen 1). To add a generation, add its entry here, curate its
/// base-experience / catch-rate tables next to <c>Gen1BaseExperience</c>, and resolve its Special model
/// (DATA_IMPORT.md §4.2).
/// </summary>
public sealed record GenerationImportScope(
    int Generation,
    string LearnsetVersionGroup,
    int MaxMoveId,
    int MaxSpeciesId
)
{
    // Red/Blue is the learnset source (Yellow's differences are not modelled); 165 is the last Gen 1 move; 151 the
    // last Gen 1 species. Guards against a stray later-generation id in PokeAPI's cumulative lists.
    private static readonly GenerationImportScope Gen1 = new(1, "red-blue", 165, 151);

    public static GenerationImportScope For(int generation) =>
        generation switch
        {
            1 => Gen1,
            _ => throw new NotSupportedException(
                $"No import scope for generation {generation}: add its learnset version group and id ranges to "
                    + nameof(GenerationImportScope)
                    + "."
            ),
        };
}
