using System.Text.Json;
using creaturegame.Attacks;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.Generation_1;

namespace PokeApiConnector.PokeAPI;

public class PokemonImport
{
    public static async Task FetchPokemonByGeneration(int generation)
    {
        string url = $"https://pokeapi.co/api/v2/generation/{generation}/";

        try
        {
            HttpResponseMessage response = await PokeApiHttp.Client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            var genResponse = JsonSerializer.Deserialize<Gen1Response>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (genResponse?.pokemon_species != null)
            {
                using var context = new PokemonDbContext();
                // Filter to only Gen 1 pokemon (IDs 1-151) because the generation endpoint returns all species associated,
                // but some might be from later gens if they have a relationship?
                // Actually for Gen 1 it should be 1-151.

                foreach (var speciesResource in genResponse.pokemon_species)
                {
                    // The species URL is like https://pokeapi.co/api/v2/pokemon-species/1/
                    // We need the pokemon data which is at https://pokeapi.co/api/v2/pokemon/1/
                    if (speciesResource.url == null)
                        continue;
                    string pokemonUrl = speciesResource.url.Replace("pokemon-species", "pokemon");
                    string speciesUrl = speciesResource.url;
                    await FetchPokemonDataByUrl(pokemonUrl, speciesUrl, context);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching pokemon by generation: {ex.Message}");
        }
    }

    private static async Task FetchPokemonDataByUrl(
        string url,
        string speciesUrl,
        PokemonDbContext context
    )
    {
        try
        {
            // Fetch Pokemon Data
            HttpResponseMessage response = await PokeApiHttp.Client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            PokeApiPokemon? pokeData = JsonSerializer.Deserialize<PokeApiPokemon>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            // Fetch Species Data for Growth Rate
            HttpResponseMessage speciesResponse = await PokeApiHttp.Client.GetAsync(speciesUrl);
            speciesResponse.EnsureSuccessStatusCode();
            string speciesJson = await speciesResponse.Content.ReadAsStringAsync();
            PokeApiPokemonSpecies? speciesData = JsonSerializer.Deserialize<PokeApiPokemonSpecies>(
                speciesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (pokeData != null && speciesData != null)
            {
                // We only want Gen 1 (1-151)
                if (pokeData.Id > 151)
                    return;

                PokemonSpecies species = MapToSpecies(pokeData, speciesData);

                var existing = await context
                    .Species.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == species.Id);
                if (existing == null)
                {
                    context.Species.Add(species);
                    Console.WriteLine($"Imported New Pokemon: {species.Name} (ID: {species.Id})");
                }
                else
                {
                    context.Species.Update(species);
                    Console.WriteLine(
                        $"Updated Existing Pokemon: {species.Name} (ID: {species.Id})"
                    );
                }

                await context.SaveChangesAsync();

                await ImportLearnset(pokeData, context);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching pokemon data from {url}: {ex.Message}");
        }
    }

    // Gen 1 is the only generation we import today; the column keeps the table multi-gen ready.
    private const int Gen1 = 1;

    // Persist the species' Gen 1 level-up learnset. Idempotent: clears this species' Gen 1
    // rows then re-inserts, so re-running the importer converges (same pattern as the
    // game-availability seeder). The (MoveId, LearnLevel) data comes straight off the
    // already-fetched /pokemon response — no extra API call.
    private static async Task ImportLearnset(PokeApiPokemon pokeData, PokemonDbContext context)
    {
        var entries = LearnsetMapper.ExtractGen1Learnset(pokeData);

        await context
            .Learnsets.Where(l => l.SpeciesId == pokeData.Id && l.Generation == Gen1)
            .ExecuteDeleteAsync();

        if (entries.Count == 0)
            return;

        context.Learnsets.AddRange(
            entries.Select(e => new PokemonLearnset
            {
                SpeciesId = pokeData.Id,
                MoveId = e.MoveId,
                LearnLevel = e.LearnLevel,
                Generation = Gen1,
            })
        );
        await context.SaveChangesAsync();
        Console.WriteLine($"  Learnset: {entries.Count} Gen 1 level-up moves for ID {pokeData.Id}");
    }

    private static PokemonSpecies MapToSpecies(
        PokeApiPokemon pokeData,
        PokeApiPokemonSpecies speciesData
    )
    {
        var species = new PokemonSpecies
        {
            Id = pokeData.Id,
            Name = pokeData.Name ?? string.Empty,
            BaseHP = pokeData.Stats?.FirstOrDefault(s => s.Stat?.Name == "hp")?.BaseStat ?? 0,
            BaseAttack =
                pokeData.Stats?.FirstOrDefault(s => s.Stat?.Name == "attack")?.BaseStat ?? 0,
            BaseDefense =
                pokeData.Stats?.FirstOrDefault(s => s.Stat?.Name == "defense")?.BaseStat ?? 0,
            BaseSpecial =
                pokeData.Stats?.FirstOrDefault(s => s.Stat?.Name == "special-attack")?.BaseStat
                ?? 0, // In Gen 1 Special Attack and Defense were one "Special" stat
            BaseSpeed = pokeData.Stats?.FirstOrDefault(s => s.Stat?.Name == "speed")?.BaseStat ?? 0,
            GrowthRate = MapGrowthRate(speciesData.GrowthRate?.Name),
            CatchRate = speciesData.CaptureRate,
            BaseExperience = pokeData.BaseExperience ?? 0,
            PokedexEntry = speciesData
                .FlavorTextEntries?.FirstOrDefault(f => f.Language?.Name == "en")
                ?.FlavorText?.Replace("\f", " ")
                .Replace("\n", " "),
        };

        // Types — prefer Gen 1-era types from past_types if available.
        // PokeAPI returns current types; past_types records what changed and when.
        // Each past_types entry means "these types were in effect up to and including
        // this generation". We pick the earliest entry covering Gen 1 (gen i–v).
        var gen1TypeSlots = Gen1TypeSlots(pokeData);

        if (gen1TypeSlots?.Count > 0)
        {
            if (Enum.TryParse<DamageType>(gen1TypeSlots[0].Type?.Name, true, out var t1))
                species.Type1 = t1;
            else
                species.Type1 = DamageType.Normal;
        }

        if (gen1TypeSlots?.Count > 1)
        {
            if (Enum.TryParse<DamageType>(gen1TypeSlots[1].Type?.Name, true, out var t2))
                species.Type2 = t2;
        }

        return species;
    }

    // Generations that predate Gen 6 (when Fairy type was added and Steel/Dark lost
    // some interactions). An entry in past_types with one of these names means the
    // listed types were the ones in use during Gen 1.
    private static readonly HashSet<string> PreGen6 =
    [
        "generation-i",
        "generation-ii",
        "generation-iii",
        "generation-iv",
        "generation-v",
    ];

    private static readonly Dictionary<string, int> GenOrder = new()
    {
        ["generation-i"] = 1,
        ["generation-ii"] = 2,
        ["generation-iii"] = 3,
        ["generation-iv"] = 4,
        ["generation-v"] = 5,
    };

    private static List<PokemonTypeSlot>? Gen1TypeSlots(PokeApiPokemon pokeData)
    {
        // If past_types has any pre-Gen-6 entry, that is the Gen 1 type. Pick the
        // entry with the lowest generation number (earliest historical record).
        var historical = pokeData
            .PastTypes?.Where(pt =>
                pt.Generation?.Name != null && PreGen6.Contains(pt.Generation.Name)
            )
            .OrderBy(pt => GenOrder.GetValueOrDefault(pt.Generation!.Name!, 99))
            .FirstOrDefault();

        return historical?.Types ?? pokeData.Types;
    }

    private static creaturegame.Creatures.GrowthRate MapGrowthRate(string? name)
    {
        return name switch
        {
            "fast" => creaturegame.Creatures.GrowthRate.Fast,
            "medium" => creaturegame.Creatures.GrowthRate.MediumFast,
            "medium-slow" => creaturegame.Creatures.GrowthRate.MediumSlow,
            "slow" => creaturegame.Creatures.GrowthRate.Slow,
            _ => creaturegame.Creatures.GrowthRate.MediumFast,
        };
    }
}
