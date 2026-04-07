namespace PokeApiConnector;
using PokeApiConnector.PokeAPI;

class Program
{
    static async Task Main(string[] args)
    {
        await MoveImport.FetchMovesByGeneration(1);
        //await MoveImport.FetchMoveData(15);
    }
}