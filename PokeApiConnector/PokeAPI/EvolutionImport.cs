using System.Text.Json;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector.PokeAPI;

/// <summary>Imports the Gen 1 evolution edges into <c>pokemon.db</c> (DATA_IMPORT.md §4.7).</summary>
public static class EvolutionImport
{
    private const int Gen1 = 1;
    private const int MaxGen1SpeciesId = 151;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Imports the Gen 1 evolution edges; returns the urls (or errors) that failed (empty when clean).</summary>
    public static async Task<IReadOnlyList<string>> ImportAllAsync(
        HttpClient? http = null,
        Func<PokemonDbContext>? createContext = null
    )
    {
        http ??= PokeApiHttp.Client;
        var failed = new List<string>();
        try
        {
            var chainUrls = await CollectChainUrlsAsync(http, failed);
            var seen = new HashSet<(int From, int To)>();
            var rows = new List<PokemonEvolution>();

            foreach (var chainUrl in chainUrls)
            {
                var chain = await FetchChainAsync(chainUrl, http);
                if (chain == null)
                {
                    failed.Add(chainUrl);
                    continue;
                }

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

            // see DATA_IMPORT.md §4.7 — a partial fetch keeps the existing edges rather than replacing them
            if (failed.Count > 0)
            {
                Console.WriteLine(
                    $"  Evolutions: {failed.Count} fetch(es) failed — existing edges left unchanged."
                );
                return failed;
            }

            using var context = (createContext ?? (() => new PokemonDbContext()))();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await context.Evolutions.Where(e => e.Generation == Gen1).ExecuteDeleteAsync();
            context.Evolutions.AddRange(rows);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            Console.WriteLine(
                $"  Evolutions: {rows.Count} Gen 1 edges from {chainUrls.Count} chains."
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error importing evolutions: {ex.Message}");
            failed.Add($"evolution import aborted: {ex.Message}");
        }

        return failed;
    }

    private static async Task<HashSet<string>> CollectChainUrlsAsync(
        HttpClient http,
        List<string> failed
    )
    {
        var urls = new HashSet<string>();
        for (int id = 1; id <= MaxGen1SpeciesId; id++)
        {
            string speciesUrl = $"https://pokeapi.co/api/v2/pokemon-species/{id}/";
            try
            {
                var response = await http.GetAsync(speciesUrl);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                var species = JsonSerializer.Deserialize<PokeApiPokemonSpecies>(json, JsonOptions);
                if (species?.EvolutionChain?.Url is not string url)
                {
                    Console.WriteLine($"  Evolution: no chain url for species {id}.");
                    failed.Add(speciesUrl);
                    continue;
                }
                urls.Add(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"  Evolution: failed to read chain url for species {id}: {ex.Message}"
                );
                failed.Add(speciesUrl);
            }
        }

        return urls;
    }

    private static async Task<PokeApiEvolutionChain?> FetchChainAsync(
        string chainUrl,
        HttpClient http
    )
    {
        try
        {
            var response = await http.GetAsync(chainUrl);
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
