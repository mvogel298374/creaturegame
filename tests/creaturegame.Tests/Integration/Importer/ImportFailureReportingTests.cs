using System.Net;
using creaturegame.Tests.TestSupport;
using PokeApiConnector;
using PokeApiConnector.Generation_1;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Integration.Importer;

public class ImportFailureReportingTests : IDisposable
{
    private const string Api = "https://pokeapi.co/api/v2";
    private readonly TempImportDatabases _db = new();

    public void Dispose() => _db.Dispose();

    private static Gen1Response Generation(string listKey, params string[] urls)
    {
        var resources = urls.Select(u => new PokeApiConnector.Generation_1.GenerationResource
            {
                url = u,
            })
            .ToList();
        return listKey == "moves"
            ? new Gen1Response { moves = resources }
            : new Gen1Response { pokemon_species = resources };
    }

    // A non-empty list whose only entry has no url — nothing importable.
    private static Gen1Response NoUsableUrl(string listKey)
    {
        var resources = new List<PokeApiConnector.Generation_1.GenerationResource> { new() };
        return listKey == "moves"
            ? new Gen1Response { moves = resources }
            : new Gen1Response { pokemon_species = resources };
    }

    [Fact]
    public async Task Moves_AFailedMoveFetch_IsReported_AndTheOthersStillImport()
    {
        string ok = $"{Api}/move/1/";
        string broken = $"{Api}/move/2/";
        var http = new FakePokeApiHandler()
            .Json($"{Api}/generation/1/", Generation("moves", ok, broken))
            .Json(
                ok,
                new PokeApiMove
                {
                    Id = 1,
                    Name = "pound",
                    Power = 40,
                    Accuracy = 100,
                    Pp = 35,
                    Type = new NamedApiResource { Name = "normal" },
                    DamageClass = new NamedApiResource { Name = "physical" },
                }
            )
            .Fail(broken)
            .Client();

        var failed = await MoveImport.FetchMovesByGeneration(1, http, _db.Moves);

        Assert.Equal([broken], failed);
        using var moves = _db.Moves();
        Assert.Single(moves.Moves, m => m.Id == 1);
    }

    [Fact]
    public async Task Moves_AMoveStill503AfterEveryRetry_IsReported_AndTheOthersStillImport()
    {
        string ok = $"{Api}/move/1/";
        string unavailable = $"{Api}/move/2/";
        var fake = new FakePokeApiHandler()
            .Json($"{Api}/generation/1/", Generation("moves", ok, unavailable))
            .Json(
                ok,
                new PokeApiMove
                {
                    Id = 1,
                    Name = "pound",
                    Power = 40,
                    Accuracy = 100,
                    Pp = 35,
                    Type = new NamedApiResource { Name = "normal" },
                    DamageClass = new NamedApiResource { Name = "physical" },
                }
            )
            .Fail(unavailable, HttpStatusCode.ServiceUnavailable);
        var waits = new List<TimeSpan>();
        var http = new HttpClient(
            new RetryHandler(
                fake,
                (wait, _) =>
                {
                    waits.Add(wait);
                    return Task.CompletedTask;
                }
            )
        );

        var failed = await MoveImport.FetchMovesByGeneration(1, http, _db.Moves);

        Assert.Equal([unavailable], failed);
        Assert.Equal(RetryHandler.MaxRetries, waits.Count);
        using var moves = _db.Moves();
        Assert.Single(moves.Moves, m => m.Id == 1);
    }

    [Fact]
    public async Task Moves_ANullMoveBody_IsReported_NotSilentlySkipped()
    {
        string empty = $"{Api}/move/1/";
        var http = new FakePokeApiHandler()
            .Json($"{Api}/generation/1/", Generation("moves", empty))
            .RawJson(empty, "null")
            .Client();

        var failed = await MoveImport.FetchMovesByGeneration(1, http, _db.Moves);

        Assert.Equal([empty], failed);
    }

    [Fact]
    public async Task Moves_AnUnreachableGenerationList_IsReported()
    {
        var http = new FakePokeApiHandler().Client();

        var failed = await MoveImport.FetchMovesByGeneration(1, http, _db.Moves);

        Assert.Equal([$"{Api}/generation/1/"], failed);
    }

    [Fact]
    public async Task Moves_AnEmptyGenerationList_IsReported()
    {
        var http = new FakePokeApiHandler()
            .Json($"{Api}/generation/1/", Generation("moves"))
            .Client();

        var failed = await MoveImport.FetchMovesByGeneration(1, http, _db.Moves);

        Assert.Equal([$"{Api}/generation/1/"], failed);
    }

    [Fact]
    public async Task Moves_AGenerationListWithNoUsableUrl_IsReported()
    {
        var http = new FakePokeApiHandler()
            .Json($"{Api}/generation/1/", NoUsableUrl("moves"))
            .Client();

        var failed = await MoveImport.FetchMovesByGeneration(1, http, _db.Moves);

        Assert.Equal([$"{Api}/generation/1/"], failed);
    }

    [Fact]
    public async Task Items_EveryUnfetchableItem_IsReported_AndTheFetchableOneImports()
    {
        string potion = $"{Api}/item/potion/";
        var http = new FakePokeApiHandler()
            .Json(
                potion,
                new PokeApiItem
                {
                    Id = 17,
                    Name = "potion",
                    Cost = 300,
                }
            )
            .Client();

        var failed = await ItemImport.ImportGen1BattleItemsAsync(http, _db.Items);

        Assert.Equal(ItemMapper.Gen1BattleItemNames.Count - 1, failed.Count);
        Assert.DoesNotContain(potion, failed);
        using var items = _db.Items();
        Assert.Single(items.Items, i => i.Id == 17);
    }

    [Fact]
    public async Task Evolutions_FailedSpeciesAndChainFetches_AreReported()
    {
        string chain = $"{Api}/evolution-chain/1/";
        var http = new FakePokeApiHandler()
            .Json(
                $"{Api}/pokemon-species/1/",
                new PokeApiPokemonSpecies
                {
                    Id = 1,
                    EvolutionChain = new EvolutionChainResource { Url = chain },
                }
            )
            .Fail(chain)
            .Client();

        var failed = await EvolutionImport.ImportAllAsync(http, _db.Pokemon);

        Assert.Contains(chain, failed);
        Assert.Contains($"{Api}/pokemon-species/2/", failed);
        Assert.DoesNotContain($"{Api}/pokemon-species/1/", failed);
    }

    [Fact]
    public async Task Species_AFailedSpeciesFetch_IsReported()
    {
        var http = new FakePokeApiHandler()
            .Json(
                $"{Api}/generation/1/",
                Generation("pokemon_species", $"{Api}/pokemon-species/1/")
            )
            .Client();

        var failed = await PokemonImport.FetchPokemonByGeneration(1, http, _db.Pokemon);

        Assert.Equal([$"{Api}/pokemon/1/"], failed);
    }

    [Fact]
    public async Task Species_AnEmptyGenerationList_IsReported()
    {
        var http = new FakePokeApiHandler()
            .Json($"{Api}/generation/1/", Generation("pokemon_species"))
            .Client();

        var failed = await PokemonImport.FetchPokemonByGeneration(1, http, _db.Pokemon);

        Assert.Equal([$"{Api}/generation/1/"], failed);
    }

    [Fact]
    public async Task Species_AGenerationListWithNoUsableUrl_IsReported()
    {
        var http = new FakePokeApiHandler()
            .Json($"{Api}/generation/1/", NoUsableUrl("pokemon_species"))
            .Client();

        var failed = await PokemonImport.FetchPokemonByGeneration(1, http, _db.Pokemon);

        Assert.Equal([$"{Api}/generation/1/"], failed);
    }

    [Fact]
    public void ExitCode_IsZeroOnlyWhenEveryStageIsClean()
    {
        StageResult clean = new("Moves", []);
        StageResult dirty = new("Items", ["x"]);

        Assert.Equal(0, ImportOutcome.ExitCode([clean, new("Species", [])]));
        Assert.Equal(1, ImportOutcome.ExitCode([clean, dirty]));
        Assert.Equal(1, ImportOutcome.ExitCode([StageResult.FromCount("Cries", 2)]));
        Assert.Equal(0, ImportOutcome.ExitCode([StageResult.FromCount("Cries", 0)]));
    }
}
