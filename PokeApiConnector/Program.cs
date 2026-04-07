namespace PokeApiConnector;
using PokeApiConnector.PokeAPI;

class Program
{
    static async Task Main(string[] args)
    {
        using (var context = new creaturegame.DB.GameDbContext())
        {
            Console.WriteLine("Ensuring database is created and schema is up to date...");
            context.EnsureDatabaseCreated();
        }

        Console.WriteLine("Importing Gen 1 Moves...");
        await MoveImport.FetchMovesByGeneration(1);
        
        Console.WriteLine("\nImporting Gen 1 Pokemon Species...");
        await PokemonImport.FetchPokemonByGeneration(1);
        
        Console.WriteLine("\nImport Complete!");
    }
}