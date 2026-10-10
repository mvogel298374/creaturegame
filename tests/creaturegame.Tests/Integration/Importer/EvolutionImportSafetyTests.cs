using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Integration.Importer;

public class EvolutionImportSafetyTests : IDisposable
{
    private const string Api = "https://pokeapi.co/api/v2";
    private const int Gen1SpeciesCount = 151;
    private readonly TempImportDatabases _db = new();

    public EvolutionImportSafetyTests()
    {
        using var context = _db.Pokemon();
        foreach (int id in new[] { 1, 2, 4, 5 })
            context.Species.Add(new PokemonSpecies { Id = id, Name = $"species-{id}" });
        context.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void SeedExistingEdge()
    {
        using var context = _db.Pokemon();
        context.Evolutions.Add(
            new PokemonEvolution
            {
                FromSpeciesId = 4,
                ToSpeciesId = 5,
                Trigger = EvolutionTrigger.Level,
                LevelThreshold = 16,
            }
        );
        context.SaveChanges();
    }

    private List<(int From, int To)> StoredEdges()
    {
        using var context = _db.Pokemon();
        return context
            .Evolutions.Select(e => new { e.FromSpeciesId, e.ToSpeciesId })
            .AsEnumerable()
            .Select(e => (e.FromSpeciesId, e.ToSpeciesId))
            .OrderBy(e => e)
            .ToList();
    }

    private static NamedApiResource Species(int id) =>
        new() { Name = $"species-{id}", Url = $"{Api}/pokemon-species/{id}/" };

    // Every Gen 1 species answers and shares one Bulbasaur → Ivysaur @16 chain (chain urls are de-duplicated).
    private static FakePokeApiHandler HealthyApi()
    {
        string chainUrl = $"{Api}/evolution-chain/1/";
        var api = new FakePokeApiHandler();
        for (int id = 1; id <= Gen1SpeciesCount; id++)
            api.Json(
                $"{Api}/pokemon-species/{id}/",
                new PokeApiPokemonSpecies
                {
                    Id = id,
                    EvolutionChain = new EvolutionChainResource { Url = chainUrl },
                }
            );
        return api.Json(
            chainUrl,
            new PokeApiEvolutionChain
            {
                Chain = new ChainLink
                {
                    Species = Species(1),
                    EvolvesTo =
                    [
                        new ChainLink
                        {
                            Species = Species(2),
                            EvolutionDetails =
                            [
                                new EvolutionDetail
                                {
                                    Trigger = new NamedApiResource { Name = "level-up" },
                                    MinLevel = 16,
                                },
                            ],
                            EvolvesTo = [],
                        },
                    ],
                },
            }
        );
    }

    [Fact]
    public async Task AFailedFetch_LeavesTheExistingEdgesUntouched_AndIsReported()
    {
        SeedExistingEdge();
        var http = new FakePokeApiHandler().Client();

        var failed = await EvolutionImport.ImportAllAsync(http, _db.Pokemon);

        Assert.NotEmpty(failed);
        Assert.Equal([(4, 5)], StoredEdges());
    }

    [Fact]
    public async Task OnePartialFailure_AlsoLeavesTheExistingEdgesUntouched()
    {
        SeedExistingEdge();
        var http = HealthyApi().Fail($"{Api}/pokemon-species/150/").Client();

        var failed = await EvolutionImport.ImportAllAsync(http, _db.Pokemon);

        Assert.Equal([$"{Api}/pokemon-species/150/"], failed);
        Assert.Equal([(4, 5)], StoredEdges());
    }

    [Fact]
    public async Task ASpeciesAnsweringWithNoChain_CountsAsAFailedFetch_AndLeavesTheEdgesUntouched()
    {
        SeedExistingEdge();
        string empty = $"{Api}/pokemon-species/150/";
        var http = HealthyApi().RawJson(empty, "null").Client();

        var failed = await EvolutionImport.ImportAllAsync(http, _db.Pokemon);

        Assert.Equal([empty], failed);
        Assert.Equal([(4, 5)], StoredEdges());
    }

    [Fact]
    public async Task ACleanFetch_ReplacesTheEdges()
    {
        SeedExistingEdge();
        var http = HealthyApi().Client();

        var failed = await EvolutionImport.ImportAllAsync(http, _db.Pokemon);

        Assert.Empty(failed);
        Assert.Equal([(1, 2)], StoredEdges());
    }

    [Fact]
    public async Task AFailedInsert_RollsTheDeleteBack()
    {
        SeedExistingEdge();
        using (var context = _db.Pokemon())
        {
            string table = context.Model.FindEntityType(typeof(PokemonEvolution))!.GetTableName()!;
            context.Database.ExecuteSqlRaw(
                $"CREATE TRIGGER fail_evolution_insert BEFORE INSERT ON \"{table}\" "
                    + "BEGIN SELECT RAISE(ABORT, 'insert refused'); END;"
            );
        }
        var http = HealthyApi().Client();

        var failed = await EvolutionImport.ImportAllAsync(http, _db.Pokemon);

        Assert.Contains(failed, f => f.StartsWith("evolution import aborted"));
        Assert.Equal([(4, 5)], StoredEdges());
    }
}
