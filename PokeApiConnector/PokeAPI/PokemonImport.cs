using System.Text.Json;
using creaturegame.Attacks;
using creaturegame.DB;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.Generation_1;

namespace PokeApiConnector.PokeAPI;

public class PokemonImport
{
    /// <summary>Imports a generation's species. Returns the PokeAPI urls of the species that failed to import (so the
    /// caller can fail the run), and lets an unsupported generation or an unreachable generation list throw — a
    /// blanket catch here once turned both into "0 imported, exit 0" (DATA_IMPORT.md §4.2).</summary>
    public static async Task<IReadOnlyList<string>> FetchPokemonByGeneration(int generation)
    {
        // First, before any I/O: an unsupported generation must throw, not import with Gen 1's numbers.
        var scope = GenerationImportScope.For(generation);
        var failed = new List<string>();
        string url = $"https://pokeapi.co/api/v2/generation/{generation}/";

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
                if (!await FetchPokemonDataByUrl(pokemonUrl, speciesUrl, context, scope))
                    failed.Add(pokemonUrl);
            }
        }

        return failed;
    }

    /// <summary>Imports one species; false when it failed (logged, and counted by the caller). A species-level
    /// failure — a network error, a missing stat — leaves that species' old row in place, which is exactly why the
    /// caller must surface the count rather than let the run look clean.</summary>
    private static async Task<bool> FetchPokemonDataByUrl(
        string url,
        string speciesUrl,
        PokemonDbContext context,
        GenerationImportScope scope
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

            if (pokeData is null || speciesData is null)
            {
                Console.WriteLine($"No usable data returned for {url} (or its species).");
                return false;
            }

            if (pokeData.Id > scope.MaxSpeciesId)
                return true; // a later generation's species in PokeAPI's cumulative list — not a failure

            PokemonSpecies species = MapToSpecies(pokeData, speciesData, scope.Generation);

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
                Console.WriteLine($"Updated Existing Pokemon: {species.Name} (ID: {species.Id})");
            }

            await context.SaveChangesAsync();

            await ImportLearnset(pokeData, context, scope.Generation);

            return true;
        }
        // A generation misconfiguration (no curated table / no scope) is not a per-species failure: let it throw.
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            Console.WriteLine($"Error fetching pokemon data from {url}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Persists the species' learnset for <paramref name="generation"/> (DATA_IMPORT.md §4.6) — the rows are
    /// keyed by <c>Generation</c>, so a later generation's import sits beside Gen 1's, never over it.</summary>
    private static async Task ImportLearnset(
        PokeApiPokemon pokeData,
        PokemonDbContext context,
        int generation
    )
    {
        var entries = LearnsetMapper.ExtractLearnset(pokeData, generation);

        await context
            .Learnsets.Where(l => l.SpeciesId == pokeData.Id && l.Generation == generation)
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
                Generation = generation,
            })
        );
        await context.SaveChangesAsync();
        int levelUp = entries.Count(e => e.Method == LearnMethod.LevelUp);
        Console.WriteLine(
            $"  Learnset: {levelUp} level-up + {entries.Count - levelUp} TM/HM Gen 1 moves for ID {pokeData.Id}"
        );
    }

    /// <summary>Maps PokeAPI's two responses to a <see cref="PokemonSpecies"/> as it was in
    /// <paramref name="generation"/>. Public for the same reason as the other mappers: so a test can drive the real
    /// wiring from DTO to column.</summary>
    public static PokemonSpecies MapToSpecies(
        PokeApiPokemon pokeData,
        PokeApiPokemonSpecies speciesData,
        int generation
    )
    {
        // Stats as they were in the target generation, not PokeAPI's current ones (DATA_IMPORT.md §4.2).
        var stats = SpeciesStatResolver.BaseStatsAsOf(pokeData, generation);

        var species = new PokemonSpecies
        {
            Id = pokeData.Id,
            Name = pokeData.Name ?? string.Empty,
            BaseHP = Require(stats, "hp", pokeData.Name),
            BaseAttack = Require(stats, "attack", pokeData.Name),
            BaseDefense = Require(stats, "defense", pokeData.Name),
            BaseSpecial = SingleSpecialAsOf(stats, generation, pokeData.Name),
            BaseSpeed = Require(stats, "speed", pokeData.Name),
            GrowthRate = MapGrowthRate(speciesData.GrowthRate?.Name),
            CatchRate = SpeciesCatchRate.For(generation, pokeData.Id),
            BaseExperience = SpeciesBaseExperience.For(generation, pokeData.Id),
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

    // A stat PokeAPI doesn't report is an import failure, never a silent 0 that would ship as a base stat.
    private static int Require(Dictionary<string, int> stats, string stat, string? name) =>
        stats.TryGetValue(stat, out var value)
            ? value
            : throw new InvalidOperationException($"{name}: PokeAPI reports no '{stat}' stat.");

    /// <summary>The value for <c>PokemonSpecies.BaseSpecial</c>. The model has one Special column, which is exactly
    /// Gen 1's shape: PokeAPI carries it as the <c>special</c> stat of the <c>generation-i</c> <c>past_stats</c> entry
    /// (not Sp. Atk, which is a different number for most species — DATA_IMPORT.md §4.2). The Gen 2 Special split is a
    /// model change (two columns), so a later generation fails here rather than getting a wrong single value.</summary>
    private static int SingleSpecialAsOf(
        Dictionary<string, int> stats,
        int generation,
        string? name
    ) =>
        generation == 1
            ? stats.TryGetValue("special", out var special)
                ? special
                : throw new InvalidOperationException(
                    $"{name}: PokeAPI has no Gen 1 'special' stat in past_stats."
                )
            : throw new NotSupportedException(
                $"Generation {generation} splits Special into two stats; the species model has one Special column."
            );

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
