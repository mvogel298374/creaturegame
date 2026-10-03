using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The importer resolves a species' base stats <i>as of a generation</i> from PokeAPI's <c>past_stats</c> — the
/// stat analogue of <c>past_types</c> (DATA_IMPORT.md §4.2). Pure mapping over a hand-built DTO; no network, no
/// database. Shapes are copied from real PokeAPI responses (Pikachu, Gyarados) so the layering rule is pinned
/// against what the API actually returns.
/// </summary>
public class SpeciesStatResolverTests
{
    private static PokemonStat S(string name, int value) =>
        new()
        {
            BaseStat = value,
            Stat = new StatResource { Name = name },
        };

    private static PastStatEntry Past(string generation, params PokemonStat[] stats) =>
        new()
        {
            Generation = new GenerationResource { Name = generation },
            Stats = [.. stats],
        };

    // Pikachu as PokeAPI serves it: modern 35/55/40/50/50/90; Gen 1 had a single Special (50), and its Defense/Sp.Def
    // were 30/40 through Gen 5.
    private static PokeApiPokemon Pikachu() =>
        new()
        {
            Id = 25,
            Name = "pikachu",
            Stats =
            [
                S("hp", 35),
                S("attack", 55),
                S("defense", 40),
                S("special-attack", 50),
                S("special-defense", 50),
                S("speed", 90),
            ],
            PastStats =
            [
                Past("generation-i", S("special", 50)),
                Past("generation-v", S("defense", 30), S("special-defense", 40)),
            ],
        };

    // Gyarados: its Gen 1 Special (100) is neither its modern Sp.Atk (60) nor an average — the reason the importer
    // must read the historical `special` stat rather than special-attack.
    private static PokeApiPokemon Gyarados() =>
        new()
        {
            Id = 130,
            Name = "gyarados",
            Stats =
            [
                S("hp", 95),
                S("attack", 125),
                S("defense", 79),
                S("special-attack", 60),
                S("special-defense", 100),
                S("speed", 81),
            ],
            PastStats = [Past("generation-i", S("special", 100))],
        };

    [Fact]
    public void Gen1_TakesTheHistoricalSpecialAndUndoesLaterDefenseBuffs()
    {
        var stats = SpeciesStatResolver.BaseStatsAsOf(Pikachu(), 1);

        Assert.Equal(30, stats["defense"]); // the generation-v entry applies to Gen 1 too
        Assert.Equal(50, stats["special"]); // Gen 1's single Special
        Assert.Equal(55, stats["attack"]); // untouched stats keep their current value
        Assert.Equal(90, stats["speed"]);
    }

    [Fact]
    public void Gen1_SpecialIsTheHistoricalStat_NotSpecialAttack()
    {
        var stats = SpeciesStatResolver.BaseStatsAsOf(Gyarados(), 1);

        Assert.Equal(100, stats["special"]);
        Assert.Equal(60, stats["special-attack"]); // the modern Sp.Atk is still reported, but is not Special
    }

    [Fact]
    public void TheEntryClosestToTheTargetGenerationWinsPerStat()
    {
        // Defense was 31 through Gen 1 and 30 through Gen 5 (same stat in two entries): Gen 1 reads the older one,
        // Gen 2 the newer one — proves entries are layered newest-to-oldest, not just "apply them all".
        var species = new PokeApiPokemon
        {
            Stats = [S("defense", 40)],
            PastStats =
            [
                Past("generation-v", S("defense", 30)),
                Past("generation-i", S("defense", 31)),
            ],
        };

        Assert.Equal(31, SpeciesStatResolver.BaseStatsAsOf(species, 1)["defense"]);
        Assert.Equal(30, SpeciesStatResolver.BaseStatsAsOf(species, 2)["defense"]);
    }

    [Fact]
    public void ADifferentGeneration_ResolvesDifferently_SoNothingIsHardCodedToGen1()
    {
        // The falsification check (GENERATION_PROFILE.md §3): if the resolver secretly meant "Gen 1", these two calls
        // would agree. Gen 6 is past every entry, so it must come back as PokeAPI's current stats.
        var gen1 = SpeciesStatResolver.BaseStatsAsOf(Pikachu(), 1);
        var gen6 = SpeciesStatResolver.BaseStatsAsOf(Pikachu(), 6);

        Assert.Equal(30, gen1["defense"]);
        Assert.Equal(40, gen6["defense"]);
        Assert.False(gen6.ContainsKey("special")); // the single Special does not exist after Gen 1
    }

    [Fact]
    public void WithNoPastStats_TheCurrentStatsAreReturned()
    {
        var species = new PokeApiPokemon { Stats = [S("hp", 10), S("speed", 20)] };

        var stats = SpeciesStatResolver.BaseStatsAsOf(species, 1);

        Assert.Equal(10, stats["hp"]);
        Assert.Equal(20, stats["speed"]);
        Assert.Equal(2, stats.Count);
    }

    [Fact]
    public void AnUnrecognisedGenerationName_IsAnImportFailure_NotSilentlyDropped()
    {
        var species = new PokeApiPokemon
        {
            Stats = [S("defense", 40)],
            PastStats = [Past("generation-x", S("defense", 1))],
        };

        Assert.Throws<InvalidOperationException>(() =>
            SpeciesStatResolver.BaseStatsAsOf(species, 1)
        );
    }
}
