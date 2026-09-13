namespace PokeApiConnector;

using PokeApiConnector.PokeAPI;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("evolutions", StringComparison.OrdinalIgnoreCase))
        {
            using (var pokemonContext = new creaturegame.DB.PokemonDbContext())
                pokemonContext.EnsureDatabaseCreated();
            Console.WriteLine("Importing Gen 1 evolutions only...");
            await EvolutionImport.ImportAllAsync();
            Console.WriteLine("\nDone.");
            return;
        }

        // DATA_IMPORT.md §3 — the Docker-build asset stage; fails loudly on a partial fetch.
        if (args.Length > 0 && args[0].Equals("assets", StringComparison.OrdinalIgnoreCase))
        {
            using (var itemsContext = new creaturegame.DB.ItemsDbContext())
                itemsContext.EnsureDatabaseCreated();

            Console.WriteLine("Downloading battle sprites...");
            int failedSprites = await SpriteDownloader.DownloadAllAsync();

            Console.WriteLine("\nDownloading item sprites...");
            int failedItemSprites = await ItemSpriteDownloader.DownloadAllAsync();

            Console.WriteLine("\nDownloading Pokémon cries (legacy 8-bit)...");
            int failedCries = await CryDownloader.DownloadAllAsync();

            int totalFailed = failedSprites + failedItemSprites + failedCries;
            if (totalFailed > 0)
            {
                Console.WriteLine(
                    $"\n{totalFailed} asset(s) failed to download — failing the build."
                );
                Environment.Exit(1);
            }

            Console.WriteLine("\nDone.");
            return;
        }

        if (args.Length > 0 && args[0].Equals("items", StringComparison.OrdinalIgnoreCase))
        {
            using (var itemsContext = new creaturegame.DB.ItemsDbContext())
                itemsContext.EnsureDatabaseCreated();
            Console.WriteLine("Importing Gen 1 battle items only...");
            await ItemImport.ImportGen1BattleItemsAsync();
            Console.WriteLine("\nDownloading item sprites...");
            await ItemSpriteDownloader.DownloadAllAsync();
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

        using (var itemsContext = new creaturegame.DB.ItemsDbContext())
        {
            Console.WriteLine("Ensuring items database is created...");
            itemsContext.EnsureDatabaseCreated();
        }

        Console.WriteLine("Importing Gen 1 Moves...");
        await MoveImport.FetchMovesByGeneration(1);

        Console.WriteLine("\nImporting Gen 1 Pokemon Species...");
        await PokemonImport.FetchPokemonByGeneration(1);

        Console.WriteLine("\nImporting Gen 1 evolutions...");
        await EvolutionImport.ImportAllAsync();

        Console.WriteLine("\nImporting Gen 1 battle items...");
        await ItemImport.ImportGen1BattleItemsAsync();

        Console.WriteLine("\nSeeding Gen 1 game availability...");
        await GameAvailabilitySeeder.SeedGen1Async();

        Console.WriteLine("\nDownloading battle sprites...");
        await SpriteDownloader.DownloadAllAsync();

        Console.WriteLine("\nDownloading item sprites...");
        await ItemSpriteDownloader.DownloadAllAsync();

        Console.WriteLine("\nDownloading Pokémon cries (legacy 8-bit)...");
        await CryDownloader.DownloadAllAsync();

        Console.WriteLine("\nImport Complete!");
    }
}
