namespace PokeApiConnector;

using PokeApiConnector.PokeAPI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        int exitCode;
        try
        {
            exitCode = await RunAsync(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nImport ABORTED - later stages did not run: {ex}");
            exitCode = 1;
        }
        return SqliteCheckpoint.FlushAll() ? exitCode : 1;
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("evolutions", StringComparison.OrdinalIgnoreCase))
        {
            using (var pokemonContext = new creaturegame.DB.PokemonDbContext())
                pokemonContext.EnsureDatabaseCreated();
            Console.WriteLine("Importing Gen 1 evolutions only...");
            var evolutions = await EvolutionImport.ImportAllAsync();
            return ImportOutcome.Report([new("Evolutions", evolutions)], "Done.");
        }

        // Species only (stats, base experience, types, learnsets) — leaves moves/items/evolutions untouched.
        if (args.Length > 0 && args[0].Equals("species", StringComparison.OrdinalIgnoreCase))
        {
            using (var pokemonContext = new creaturegame.DB.PokemonDbContext())
                pokemonContext.EnsureDatabaseCreated();
            Console.WriteLine("Importing Gen 1 species only...");
            var species = await PokemonImport.FetchPokemonByGeneration(1);
            return ImportOutcome.Report([new("Species", species)], "Done.");
        }

        // Offline: re-apply the curated TM/HM floors (Attack.MinLevel) to the existing moves.db — no network.
        if (args.Length > 0 && args[0].Equals("move-levels", StringComparison.OrdinalIgnoreCase))
        {
            using var moveContext = new creaturegame.DB.MovesDbContext();
            moveContext.EnsureDatabaseCreated();
            int applied = await MoveMinLevels.ApplyToDatabaseAsync(moveContext);
            Console.WriteLine($"Applied MinLevel floors to {applied} moves.");
            return 0;
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

            return ImportOutcome.Report(
                [
                    StageResult.FromCount("Battle sprites", failedSprites),
                    StageResult.FromCount("Item sprites", failedItemSprites),
                    StageResult.FromCount("Cries", failedCries),
                ],
                "Done."
            );
        }

        if (args.Length > 0 && args[0].Equals("items", StringComparison.OrdinalIgnoreCase))
        {
            using (var itemsContext = new creaturegame.DB.ItemsDbContext())
                itemsContext.EnsureDatabaseCreated();
            Console.WriteLine("Importing Gen 1 battle items only...");
            var items = await ItemImport.ImportGen1BattleItemsAsync();
            Console.WriteLine("\nDownloading item sprites...");
            int failedItemSprites = await ItemSpriteDownloader.DownloadAllAsync();
            return ImportOutcome.Report(
                [new("Items", items), StageResult.FromCount("Item sprites", failedItemSprites)],
                "Done."
            );
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

        // A record failure doesn't stop later stages; a thrown exception aborts the run (see Main).
        var stages = new List<StageResult>();

        Console.WriteLine("Importing Gen 1 Moves...");
        stages.Add(new("Moves", await MoveImport.FetchMovesByGeneration(1)));

        Console.WriteLine("\nImporting Gen 1 Pokemon Species...");
        stages.Add(new("Species", await PokemonImport.FetchPokemonByGeneration(1)));

        Console.WriteLine("\nImporting Gen 1 evolutions...");
        stages.Add(new("Evolutions", await EvolutionImport.ImportAllAsync()));

        Console.WriteLine("\nImporting Gen 1 battle items...");
        stages.Add(new("Items", await ItemImport.ImportGen1BattleItemsAsync()));

        Console.WriteLine("\nSeeding Gen 1 game availability...");
        await GameAvailabilitySeeder.SeedGen1Async();

        Console.WriteLine("\nDownloading battle sprites...");
        stages.Add(
            StageResult.FromCount("Battle sprites", await SpriteDownloader.DownloadAllAsync())
        );

        Console.WriteLine("\nDownloading item sprites...");
        stages.Add(
            StageResult.FromCount("Item sprites", await ItemSpriteDownloader.DownloadAllAsync())
        );

        Console.WriteLine("\nDownloading Pokémon cries (legacy 8-bit)...");
        stages.Add(StageResult.FromCount("Cries", await CryDownloader.DownloadAllAsync()));

        return ImportOutcome.Report(stages, "Import Complete!");
    }
}
