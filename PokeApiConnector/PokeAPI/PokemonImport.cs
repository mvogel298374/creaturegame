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

                foreach (var speciesResource in genResponse.pokemon_species)
                {
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
            HttpResponseMessage response = await PokeApiHttp.Client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync();
            PokeApiPokemon? pokeData = JsonSerializer.Deserialize<PokeApiPokemon>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            HttpResponseMessage speciesResponse = await PokeApiHttp.Client.GetAsync(speciesUrl);
            speciesResponse.EnsureSuccessStatusCode();
            string speciesJson = await speciesResponse.Content.ReadAsStringAsync();
            PokeApiPokemonSpecies? speciesData = JsonSerializer.Deserialize<PokeApiPokemonSpecies>(
                speciesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (pokeData != null && speciesData != null)
            {
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

    private const int Gen1 = 1; // Learnsets is already keyed by Generation — TODO.md → Multi-Generation

    /// <summary>Persists the species' Gen 1 learnset (DATA_IMPORT.md §4.6).</summary>
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
                Method = e.Method,
                Generation = Gen1,
            })
        );
        await context.SaveChangesAsync();
        int levelUp = entries.Count(e => e.Method == LearnMethod.LevelUp);
        Console.WriteLine(
            $"  Learnset: {levelUp} level-up + {entries.Count - levelUp} TM/HM Gen 1 moves for ID {pokeData.Id}"
        );
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
                ?? 0, // Gen 1's one Special stat — deliberately special-attack, not an average (DATA_IMPORT.md §4.2)
            BaseSpeed = pokeData.Stats?.FirstOrDefault(s => s.Stat?.Name == "speed")?.BaseStat ?? 0,
            GrowthRate = MapGrowthRate(speciesData.GrowthRate?.Name),
            CatchRate = speciesData.CaptureRate,
            BaseExperience = pokeData.BaseExperience ?? 0,
            PokedexEntry = speciesData
                .FlavorTextEntries?.FirstOrDefault(f => f.Language?.Name == "en")
                ?.FlavorText?.Replace("\f", " ")
                .Replace("\n", " "),
        };

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

    // Pre-Gen-6 generation names — an entry here means the listed types were Gen 1's (DATA_IMPORT.md §4.2).
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
