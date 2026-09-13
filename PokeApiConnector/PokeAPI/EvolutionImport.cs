using System.Text.Json;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector.PokeAPI;

/// <summary>Imports the Gen 1 evolution edges into <c>pokemon.db</c> (DATA_IMPORT.md §4.7).</summary>
public static class EvolutionImport
{
    private const int Gen1 = 1; // Evolutions is already keyed by Generation — TODO.md → Multi-Generation
    private const int MaxGen1SpeciesId = 151;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task ImportAllAsync()
    {
        try
        {
            using var context = new PokemonDbContext();

            await context.Evolutions.Where(e => e.Generation == Gen1).ExecuteDeleteAsync();

            var chainUrls = await CollectChainUrlsAsync();
            var seen = new HashSet<(int From, int To)>();
            var rows = new List<PokemonEvolution>();

            foreach (var chainUrl in chainUrls)
            {
                var chain = await FetchChainAsync(chainUrl);
                if (chain == null)
                    continue;

                foreach (var edge in EvolutionMapper.ExtractGen1Edges(chain))
                {
                    if (!seen.Add((edge.FromSpeciesId, edge.ToSpeciesId)))
                        continue;

                    rows.Add(
                        new PokemonEvolution
                        {
                            FromSpeciesId = edge.FromSpeciesId,
                            ToSpeciesId = edge.ToSpeciesId,
                            Trigger = edge.Trigger,
                            LevelThreshold = edge.LevelThreshold,
                            StoneItemId = edge.StoneItemId,
                            Generation = Gen1,
                        }
                    );
                }
            }

            context.Evolutions.AddRange(rows);
            await context.SaveChangesAsync();
            Console.WriteLine(
                $"  Evolutions: {rows.Count} Gen 1 edges from {chainUrls.Count} chains."
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error importing evolutions: {ex.Message}");
        }
    }

    private static async Task<HashSet<string>> CollectChainUrlsAsync()
    {
        var urls = new HashSet<string>();
        for (int id = 1; id <= MaxGen1SpeciesId; id++)
        {
            string speciesUrl = $"https://pokeapi.co/api/v2/pokemon-species/{id}/";
            try
            {
                var response = await PokeApiHttp.Client.GetAsync(speciesUrl);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var species = JsonSerializer.Deserialize<PokeApiPokemonSpecies>(json, JsonOptions);
                if (species?.EvolutionChain?.Url is string url)
                    urls.Add(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"  Evolution: failed to read chain url for species {id}: {ex.Message}"
                );
            }
        }

        return urls;
    }

    private static async Task<PokeApiEvolutionChain?> FetchChainAsync(string chainUrl)
    {
        try
        {
            var response = await PokeApiHttp.Client.GetAsync(chainUrl);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<PokeApiEvolutionChain>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Evolution: failed to fetch chain {chainUrl}: {ex.Message}");
            return null;
        }
    }
}
