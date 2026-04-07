using System.Text.Json;
using PokeApiConnector.Generation_1;
using creaturegame.Attacks;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector.PokeAPI;

public class PokemonImport
{
    public static async Task FetchPokemonByGeneration(int generation)
    {
        string url = $"https://pokeapi.co/api/v2/generation/{generation}/";
        using HttpClient client = new HttpClient();

        try
        {
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            var genResponse = JsonSerializer.Deserialize<Gen1Response>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (genResponse?.pokemon_species != null)
            {
                using var context = new GameDbContext();
                // Filter to only Gen 1 pokemon (IDs 1-151) because the generation endpoint returns all species associated, 
                // but some might be from later gens if they have a relationship? 
                // Actually for Gen 1 it should be 1-151.
                
                foreach (var speciesResource in genResponse.pokemon_species)
                {
                    // The species URL is like https://pokeapi.co/api/v2/pokemon-species/1/
                    // We need the pokemon data which is at https://pokeapi.co/api/v2/pokemon/1/
                    string pokemonUrl = speciesResource.url.Replace("pokemon-species", "pokemon");
                    await FetchPokemonDataByUrl(pokemonUrl, context);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching pokemon by generation: {ex.Message}");
        }
    }

    private static async Task FetchPokemonDataByUrl(string url, GameDbContext context)
    {
        using HttpClient client = new HttpClient();
        try
        {
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            PokeApiPokemon pokeData = JsonSerializer.Deserialize<PokeApiPokemon>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (pokeData != null)
            {
                // We only want Gen 1 (1-151)
                if (pokeData.Id > 151) return;

                PokemonSpecies species = MapToSpecies(pokeData);
                
                var existing = await context.Species.AsNoTracking().FirstOrDefaultAsync(s => s.Id == species.Id);
                if (existing == null)
                {
                    context.Species.Add(species);
                    Console.WriteLine($"Imported New Pokemon: {species.Name} (ID: {species.Id})");
                }
                else
                {
                    context.Species.Update(species);
                    Console.WriteLine($"Updated Existing Pokemon: {species.Name} (ID: {species.Id})");
                }
                
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching pokemon data from {url}: {ex.Message}");
        }
    }

    private static PokemonSpecies MapToSpecies(PokeApiPokemon pokeData)
    {
        var species = new PokemonSpecies
        {
            Id = pokeData.Id,
            Name = pokeData.Name,
            BaseHP = pokeData.Stats.FirstOrDefault(s => s.Stat.Name == "hp")?.BaseStat ?? 0,
            BaseAttack = pokeData.Stats.FirstOrDefault(s => s.Stat.Name == "attack")?.BaseStat ?? 0,
            BaseDefense = pokeData.Stats.FirstOrDefault(s => s.Stat.Name == "defense")?.BaseStat ?? 0,
            BaseSpecial = pokeData.Stats.FirstOrDefault(s => s.Stat.Name == "special-attack")?.BaseStat ?? 0, // In Gen 1 Special Attack and Defense were one "Special" stat
            BaseSpeed = pokeData.Stats.FirstOrDefault(s => s.Stat.Name == "speed")?.BaseStat ?? 0
        };

        // Types
        if (pokeData.Types.Count > 0)
        {
            if (Enum.TryParse<DamageType>(pokeData.Types[0].Type.Name, true, out var t1))
                species.Type1 = t1;
            else
                species.Type1 = DamageType.Normal;
        }

        if (pokeData.Types.Count > 1)
        {
            if (Enum.TryParse<DamageType>(pokeData.Types[1].Type.Name, true, out var t2))
                species.Type2 = t2;
        }

        return species;
    }
}
