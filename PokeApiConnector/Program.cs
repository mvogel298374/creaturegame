namespace PokeApiConnector;

using PokeApiConnector.PokeAPI;

class Program
{
    static async Task Main(string[] args)
    {
        // Re-run a single stage without the full (network-heavy) import. Each stage is idempotent.
        if (args.Length > 0 && args[0].Equals("evolutions", StringComparison.OrdinalIgnoreCase))
        {
            using (var pokemonContext = new creaturegame.DB.PokemonDbContext())
                pokemonContext.EnsureDatabaseCreated();
            Console.WriteLine("Importing Gen 1 evolutions only...");
            await EvolutionImport.ImportAllAsync();
            Console.WriteLine("\nDone.");
            return;
        }

        using (var moveContext = new creaturegame.DB.MovesDbContext())
        {
            Console.WriteLine("Ensuring moves database is created...");
            moveContext.EnsureDatabaseCreated();
        }

        using (var pokemonContext = new creaturegame.DB.PokemonDbContext())
        {
            Console.WriteLine("Ensuring pokemon database is created...");
            pokemonContext.EnsureDatabaseCreated();
        }

        Console.WriteLine("Importing Gen 1 Moves...");
        await MoveImport.FetchMovesByGeneration(1);

        Console.WriteLine("\nImporting Gen 1 Pokemon Species...");
        await PokemonImport.FetchPokemonByGeneration(1);

        Console.WriteLine("\nImporting Gen 1 evolutions...");
        await EvolutionImport.ImportAllAsync();

        Console.WriteLine("\nSeeding Gen 1 game availability...");
        await GameAvailabilitySeeder.SeedGen1Async();

        Console.WriteLine("\nDownloading battle sprites...");
        await SpriteDownloader.DownloadAllAsync();

        Console.WriteLine("\nDownloading Pokémon cries (legacy 8-bit)...");
        await CryDownloader.DownloadAllAsync();

        Console.WriteLine("\nImport Complete!");
    }
}
