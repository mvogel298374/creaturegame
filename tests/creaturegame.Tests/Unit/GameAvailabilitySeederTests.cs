using creaturegame.DB;
using creaturegame.Tests.TestSupport;
using PokeApiConnector;

namespace creaturegame.Tests.Unit;

public class GameAvailabilitySeederTests
{
    [Fact]
    public async Task ReSeedingUnchangedRows_KeepsTheirIds_AndWritesNothing()
    {
        using var db = new TempImportDatabases();
        using (var context = db.Pokemon())
        {
            foreach (int id in Enumerable.Range(1, 151))
                context.Species.Add(new PokemonSpecies { Id = id, Name = $"species-{id}" });
            context.SaveChanges();
        }
        await GameAvailabilitySeeder.SeedGen1Async(db.Pokemon);
        var idsAfterFirst = StoredIds(db);
        using var probe = db.WatchPokemon();

        await GameAvailabilitySeeder.SeedGen1Async(db.Pokemon);

        Assert.Equal(GameAvailabilitySeeder.BuildGen1Rows().Count, idsAfterFirst.Count);
        Assert.Equal(idsAfterFirst, StoredIds(db));
        Assert.False(probe.SawAWrite, "re-seeding unchanged rows must not write");
    }

    private static List<int> StoredIds(TempImportDatabases db)
    {
        using var context = db.Pokemon();
        return context.GameAvailability.Select(r => r.Id).OrderBy(id => id).ToList();
    }

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
    [InlineData(108)] // Lickitung
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
