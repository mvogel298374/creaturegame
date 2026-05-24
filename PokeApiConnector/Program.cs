namespace PokeApiConnector;
using PokeApiConnector.PokeAPI;

class Program
{
    static async Task Main(string[] args)
    {
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
        
        Console.WriteLine("\nDownloading battle sprites...");
        await SpriteDownloader.DownloadAllAsync();

        Console.WriteLine("\nImport Complete!");
    }
}