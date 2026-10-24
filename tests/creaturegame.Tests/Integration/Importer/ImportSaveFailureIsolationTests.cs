using creaturegame.Attacks;
using creaturegame.DB;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Integration.Importer;

public class ImportSaveFailureIsolationTests : IDisposable
{
    private const string Api = "https://pokeapi.co/api/v2";
    private readonly TempImportDatabases _db = new();

    public void Dispose() => _db.Dispose();

    private static void RefuseInsertOfId<TEntity>(DbContext context, int id)
    {
        string table = context.Model.FindEntityType(typeof(TEntity))!.GetTableName()!;
        context.Database.ExecuteSqlRaw(
            $"CREATE TRIGGER refuse_{id} BEFORE INSERT ON \"{table}\" WHEN NEW.\"Id\" = {id} "
                + "BEGIN SELECT RAISE(ABORT, 'insert refused'); END;"
        );
    }

    private static PokeApiConnector.Generation_1.Gen1Response Generation(
        bool species,
        params string[] urls
    )
    {
        var resources = urls.Select(u => new PokeApiConnector.Generation_1.GenerationResource
            {
                url = u,
            })
            .ToList();
        return species ? new() { pokemon_species = resources } : new() { moves = resources };
    }

    private static PokeApiMove Move(int id, string name) =>
        new()
        {
            Id = id,
            Name = name,
            Power = 40,
            Accuracy = 100,
            Pp = 35,
            Type = new NamedApiResource { Name = "normal" },
            DamageClass = new NamedApiResource { Name = "physical" },
        };

    private static PokemonStat Stat(string name, int value) =>
        new()
        {
            BaseStat = value,
            Stat = new StatResource { Name = name },
        };

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
                    Type = new TypeResource { Name = "normal" },
                },
            ],
        };

    [Fact]
    public async Task Moves_AFailedSave_DoesNotFailTheMovesAfterIt()
    {
        string first = $"{Api}/move/1/";
        string second = $"{Api}/move/2/";
        var http = new FakePokeApiHandler()
            .Json($"{Api}/generation/1/", Generation(false, first, second))
            .Json(first, Move(1, "pound"))
            .Json(second, Move(2, "karate-chop"))
            .Client();
        using (var context = _db.Moves())
            RefuseInsertOfId<Attack>(context, 1);

        var failed = await MoveImport.FetchMovesByGeneration(1, http, _db.Moves);

        Assert.Equal([first], failed);
        using var moves = _db.Moves();
        Assert.Equal([2], moves.Moves.Select(m => m.Id).ToList());
    }

    [Fact]
    public async Task Items_AFailedSave_DoesNotFailTheItemsAfterIt()
    {
        string firstName = ItemMapper.Gen1BattleItemNames.ElementAt(0);
        string secondName = ItemMapper.Gen1BattleItemNames.ElementAt(1);
        string first = $"{Api}/item/{firstName}/";
        var http = new FakePokeApiHandler()
            .Json(first, new PokeApiItem { Id = 901, Name = firstName })
            .Json($"{Api}/item/{secondName}/", new PokeApiItem { Id = 902, Name = secondName })
            .Client();
        using (var context = _db.Items())
            RefuseInsertOfId<Item>(context, 901);

        var failed = await ItemImport.ImportGen1BattleItemsAsync(http, _db.Items);

        Assert.Contains(first, failed);
        using var items = _db.Items();
        Assert.Equal([902], items.Items.Select(i => i.Id).ToList());
    }

    [Fact]
    public async Task Species_AFailedSave_DoesNotFailTheSpeciesAfterIt()
    {
        var http = new FakePokeApiHandler()
            .Json(
                $"{Api}/generation/1/",
                Generation(true, $"{Api}/pokemon-species/1/", $"{Api}/pokemon-species/2/")
            )
            .Json($"{Api}/pokemon/1/", Pokemon(1))
            .Json($"{Api}/pokemon-species/1/", new PokeApiPokemonSpecies { Id = 1 })
            .Json($"{Api}/pokemon/2/", Pokemon(2))
            .Json($"{Api}/pokemon-species/2/", new PokeApiPokemonSpecies { Id = 2 })
            .Client();
        using (var context = _db.Pokemon())
            RefuseInsertOfId<PokemonSpecies>(context, 1);

        var failed = await PokemonImport.FetchPokemonByGeneration(1, http, _db.Pokemon);

        Assert.Equal([$"{Api}/pokemon/1/"], failed);
        using var pokemon = _db.Pokemon();
        Assert.Equal([2], pokemon.Species.Select(s => s.Id).ToList());
    }
}
