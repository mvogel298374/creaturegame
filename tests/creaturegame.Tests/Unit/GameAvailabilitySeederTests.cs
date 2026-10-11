using PokeApiConnector;

namespace creaturegame.Tests.Unit;

public class GameAvailabilitySeederTests
{
    private static string? YellowType(int speciesId) =>
        GameAvailabilitySeeder
            .BuildGen1Rows()
            .SingleOrDefault(r => r.SpeciesId == speciesId && r.GameVersion == "yellow")
            ?.AvailabilityType;

    [Theory]
    [InlineData(27)] // Sandshrew
    [InlineData(28)] // Sandslash
    [InlineData(43)] // Oddish
    [InlineData(44)] // Gloom
    [InlineData(56)] // Mankey
    [InlineData(57)] // Primeape
    [InlineData(58)] // Growlithe
    [InlineData(69)] // Bellsprout
    [InlineData(70)] // Weepinbell
    public void YellowHasTheLinesItsWildTablesInclude(int speciesId) =>
        Assert.Equal("Wild", YellowType(speciesId));

    [Theory]
    [InlineData(37)] // Vulpix
    [InlineData(38)] // Ninetales
    public void YellowVulpixLineIsAGameCornerPrize(int speciesId) =>
        Assert.Equal("GameCorner", YellowType(speciesId));

    [Theory]
    [InlineData(13)] // Weedle
    [InlineData(23)] // Ekans
    [InlineData(52)] // Meowth
    [InlineData(109)] // Koffing
    [InlineData(124)] // Jynx
    public void YellowStillExcludesTradeOnlySpecies(int speciesId) =>
        Assert.Null(YellowType(speciesId));
}
