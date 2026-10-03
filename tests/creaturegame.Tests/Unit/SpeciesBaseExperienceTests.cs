using creaturegame.Tests.TestSupport;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// PokeAPI has no history for <c>base_experience</c>, so Gen 1's values are a curated table (pret/pokered). These
/// pin its shape and the generation-keyed lookup in front of it — values taken from pokered's
/// <c>db N ; base exp</c> lines. No network, no database.
/// </summary>
public class SpeciesBaseExperienceTests
{
    [Theory]
    [InlineData(1, 64)] // Bulbasaur
    [InlineData(25, 82)] // Pikachu — modern 112
    [InlineData(113, 255)] // Chansey — modern 395
    [InlineData(129, 20)] // Magikarp — modern 40
    [InlineData(151, 64)] // Mew — modern 270
    public void Gen1_ReturnsTheOriginalGameValue(int speciesId, int expected) =>
        Assert.Equal(expected, SpeciesBaseExperience.For(1, speciesId));

    [Fact]
    public void Gen1_EveryRowMatchesThePokeredSnapshot_WithoutTheDatabase()
    {
        // A typo in the curated table would otherwise pass every test until the next re-import hit the DB contract.
        foreach (var want in PokeredGen1Species.All)
            Assert.Equal(want.BaseExperience, SpeciesBaseExperience.For(1, want.Id));
    }

    [Fact]
    public void Gen1_CoversEveryDexIdAndNothingIsZero()
    {
        for (int id = 1; id <= 151; id++)
            Assert.True(
                SpeciesBaseExperience.For(1, id) > 0,
                $"species {id} has no base experience"
            );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(152)]
    public void Gen1_RejectsIdsOutsideTheDex(int speciesId) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeciesBaseExperience.For(1, speciesId));

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void AGenerationWithoutACuratedTable_FailsLoudly_InsteadOfImportingTheModernValue(
        int generation
    ) => Assert.Throws<NotSupportedException>(() => SpeciesBaseExperience.For(generation, 25));
}
