using creaturegame.DB;
using creaturegame.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Integration.Importer;

public class LearnsetImportSafetyTests : IDisposable
{
    private const string Api = "https://pokeapi.co/api/v2";
    private readonly TempImportDatabases _db = new();

    public LearnsetImportSafetyTests()
    {
        using var context = _db.Pokemon();
        context.Species.Add(new PokemonSpecies { Id = 1, Name = "bulbasaur" });
        context.Learnsets.Add(
            new PokemonLearnset
            {
                SpeciesId = 1,
                MoveId = 33,
                LearnLevel = 1,
            }
        );
        context.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static PokemonStat Stat(string name, int value) =>
        new()
        {
            BaseStat = value,
            Stat = new StatResource { Name = name },
        };

    // A species learning Vine Whip at 13 in Red/Blue — a different learnset from the seeded Tackle row.
    private static PokeApiPokemon Pokemon(int id) =>
        new()
        {
            Id = id,
            Name = $"species-{id}",
            BaseExperience = 64,
            Stats =
            [
                Stat("hp", 45),
                Stat("attack", 49),
                Stat("defense", 49),
                Stat("special-attack", 65),
                Stat("special-defense", 65),
                Stat("speed", 45),
            ],
            PastStats =
            [
                new PastStatEntry
                {
                    Generation = new GenerationResource { Name = "generation-i" },
                    Stats = [Stat("special", 65)],
                },
            ],
            Types =
            [
                new PokemonTypeSlot
                {
                    Slot = 1,
                    Type = new TypeResource { Name = "grass" },
                },
            ],
            Moves =
            [
                new PokeApiMoveEntry
                {
                    Move = new NamedApiResource { Name = "vine-whip", Url = $"{Api}/move/22/" },
                    VersionGroupDetails =
                    [
                        new MoveVersionGroupDetail
                        {
                            LevelLearnedAt = 13,
                            MoveLearnMethod = new NamedApiResource { Name = "level-up" },
                            VersionGroup = new NamedApiResource { Name = "red-blue" },
                        },
                    ],
                },
            ],
        };

    private static HttpClient ApiServing(params int[] speciesIds)
    {
        var api = new FakePokeApiHandler().Json(
            $"{Api}/generation/1/",
            new PokeApiConnector.Generation_1.Gen1Response
            {
                pokemon_species = speciesIds
                    .Select(id => new PokeApiConnector.Generation_1.GenerationResource
                    {
                        url = $"{Api}/pokemon-species/{id}/",
                    })
                    .ToList(),
            }
        );
        foreach (int id in speciesIds)
            api.Json($"{Api}/pokemon/{id}/", Pokemon(id))
                .Json($"{Api}/pokemon-species/{id}/", new PokeApiPokemonSpecies { Id = id });
        return api.Client();
    }

    private List<int> StoredMoveIds(int speciesId)
    {
        using var context = _db.Pokemon();
        return context
            .Learnsets.Where(l => l.SpeciesId == speciesId)
            .Select(l => l.MoveId)
            .ToList();
    }

    private PokemonSpecies StoredSpecies(int speciesId)
    {
        using var context = _db.Pokemon();
        return context.Species.AsNoTracking().Single(s => s.Id == speciesId);
    }

    [Fact]
    public async Task AFailedLearnsetInsert_KeepsTheOldSpeciesRowAndLearnset_ReportsTheSpecies_AndTheNextSpeciesStillImports()
    {
        using (var context = _db.Pokemon())
        {
            string table = context.Model.FindEntityType(typeof(PokemonLearnset))!.GetTableName()!;
            context.Database.ExecuteSqlRaw(
                $"CREATE TRIGGER refuse_learnset BEFORE INSERT ON \"{table}\" "
                    + "WHEN NEW.\"SpeciesId\" = 1 BEGIN SELECT RAISE(ABORT, 'insert refused'); END;"
            );
        }

        var failed = await PokemonImport.FetchPokemonByGeneration(1, ApiServing(1, 2), _db.Pokemon);

        Assert.Equal([$"{Api}/pokemon/1/"], failed);
        Assert.Equal([33], StoredMoveIds(1));
        var unchanged = StoredSpecies(1);
        Assert.Equal("bulbasaur", unchanged.Name);
        Assert.Equal(0, unchanged.BaseHP);
        Assert.Equal([22], StoredMoveIds(2));
        Assert.Equal(45, StoredSpecies(2).BaseHP);
    }

    [Fact]
    public async Task ACleanImport_ReplacesTheLearnset()
    {
        var failed = await PokemonImport.FetchPokemonByGeneration(1, ApiServing(1), _db.Pokemon);

        Assert.Empty(failed);
        Assert.Equal([22], StoredMoveIds(1));
    }
}
