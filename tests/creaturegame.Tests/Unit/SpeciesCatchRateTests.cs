using creaturegame.Tests.TestSupport;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Gen 1 catch rates are a curated table (pret/pokered <c>db N ; catch rate</c>) because PokeAPI keeps no history for
/// them; these pin the table and the generation-keyed lookup in front of it. No network, no database.
/// </summary>
public class SpeciesCatchRateTests
{
    [Theory]
    [InlineData(20, 90)] // Raticate — modern capture_rate 127, the one species that differs
    [InlineData(113, 30)] // Chansey
    [InlineData(150, 3)] // Mewtwo
    public void Gen1_ReturnsTheOriginalGameValue(int speciesId, int expected) =>
        Assert.Equal(expected, SpeciesCatchRate.For(1, speciesId));

    [Fact]
    public void Gen1_EveryRowMatchesThePokeredSnapshot_WithoutTheDatabase()
    {
        foreach (var want in PokeredGen1Species.All)
            Assert.Equal(want.CatchRate, SpeciesCatchRate.For(1, want.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(152)]
    public void Gen1_RejectsIdsOutsideTheDex(int speciesId) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SpeciesCatchRate.For(1, speciesId));

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void AGenerationWithoutACuratedTable_FailsLoudly_InsteadOfImportingTheModernValue(
        int generation
    ) => Assert.Throws<NotSupportedException>(() => SpeciesCatchRate.For(generation, 25));
}
