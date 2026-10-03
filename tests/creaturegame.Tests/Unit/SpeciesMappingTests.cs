using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The importer's whole DTO → <c>PokemonSpecies</c> wiring for a generation (<see cref="PokemonImport.MapToSpecies"/>):
/// the resolver, the curated tables and the single-Special rule meeting at the right columns. The data contract test
/// pins the DB; this pins that a RE-IMPORT still produces it — a regression in the wiring (e.g. reading Sp.Atk again)
/// would otherwise only show up the next time someone re-imports. No network, no database.
/// </summary>
public class SpeciesMappingTests
{
    private static PokemonStat S(string name, int value) =>
        new()
        {
            BaseStat = value,
            Stat = new StatResource { Name = name },
        };

    // Chansey as PokeAPI serves it today: modern Sp.Atk 35 / base_experience 395 / capture_rate 30, with the
    // generation-i `special` carried in past_stats.
    private static PokeApiPokemon Chansey() =>
        new()
        {
            Id = 113,
            Name = "chansey",
            BaseExperience = 395,
            Stats =
            [
                S("hp", 250),
                S("attack", 5),
                S("defense", 5),
                S("special-attack", 35),
                S("special-defense", 105),
                S("speed", 50),
            ],
            PastStats =
            [
                new PastStatEntry
                {
                    Generation = new GenerationResource { Name = "generation-i" },
                    Stats = [S("special", 105)],
                },
            ],
            Types =
            [
                new PokemonTypeSlot
                {
                    Slot = 1,
                    Type = new TypeResource { Name = "normal" },
                },
            ],
        };

    private static PokeApiPokemonSpecies ChanseySpecies() =>
        new()
        {
            Id = 113,
            Name = "chansey",
            CaptureRate = 30,
            GrowthRate = new GrowthRateResource { Name = "fast" },
        };

    [Fact]
    public void Gen1_MapsChanseyToItsPokeredRow_NotPokeApisModernOne()
    {
        var species = PokemonImport.MapToSpecies(Chansey(), ChanseySpecies(), 1);
        var pokered = PokeredGen1Species.All.Single(r => r.Id == 113);

        Assert.Equal(
            (pokered.Hp, pokered.Attack, pokered.Defense, pokered.Speed, pokered.Special),
            (
                species.BaseHP,
                species.BaseAttack,
                species.BaseDefense,
                species.BaseSpeed,
                species.BaseSpecial
            )
        );
        Assert.Equal(pokered.BaseExperience, species.BaseExperience); // 255, not the modern 395
        Assert.Equal(DamageType.Normal, species.Type1);
        Assert.Equal(GrowthRate.Fast, species.GrowthRate);
    }

    [Fact]
    public void Gen1_CatchRateComesFromTheCuratedTable_NotPokeApisCaptureRate()
    {
        // Raticate is the one Gen 1 species whose capture_rate differs from the original game (127 vs 90).
        var raticate = new PokeApiPokemon
        {
            Id = 20,
            Name = "raticate",
            Stats =
            [
                S("hp", 55),
                S("attack", 81),
                S("defense", 60),
                S("special-attack", 50),
                S("special-defense", 70),
                S("speed", 97),
            ],
            PastStats =
            [
                new PastStatEntry
                {
                    Generation = new GenerationResource { Name = "generation-i" },
                    Stats = [S("special", 50)],
                },
            ],
        };

        var species = PokemonImport.MapToSpecies(
            raticate,
            new PokeApiPokemonSpecies { Id = 20, CaptureRate = 127 },
            1
        );

        Assert.Equal(90, species.CatchRate);
    }

    [Fact]
    public void AMissingStatIsAnImportFailure_NotASilentZero()
    {
        var noSpeed = Chansey();
        noSpeed.Stats = noSpeed.Stats!.Where(s => s.Stat!.Name != "speed").ToList();

        Assert.Throws<InvalidOperationException>(() =>
            PokemonImport.MapToSpecies(noSpeed, ChanseySpecies(), 1)
        );
    }

    [Fact]
    public void AMissingGen1SpecialIsAnImportFailure_NotAFallbackToSpAtk()
    {
        var noSpecial = Chansey();
        noSpecial.PastStats = [];

        Assert.Throws<InvalidOperationException>(() =>
            PokemonImport.MapToSpecies(noSpecial, ChanseySpecies(), 1)
        );
    }

    [Fact]
    public void AGenerationWithoutSupport_Throws_InsteadOfMappingWithGen1Numbers() =>
        Assert.Throws<NotSupportedException>(() =>
            PokemonImport.MapToSpecies(Chansey(), ChanseySpecies(), 2)
        );

    [Fact]
    public async Task FetchingAnUnsupportedGeneration_Throws_BeforeAnyNetworkOrDatabaseWork()
    {
        // Used to be swallowed by a blanket catch: "0 imported", exit 0. The scope is resolved first, so this needs
        // neither network nor database — and the exception reaches the caller.
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            PokemonImport.FetchPokemonByGeneration(2)
        );
    }

    [Fact]
    public void Gen1ImportScope_IsThe151SpeciesRedBlueScope()
    {
        var scope = GenerationImportScope.For(1);

        Assert.Equal(
            ("red-blue", 165, 151),
            (scope.LearnsetVersionGroup, scope.MaxMoveId, scope.MaxSpeciesId)
        );
    }
}
