using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="EncounterFactory.FilterByMinLevel"/> — the pure core of the min-level filter
/// (<c>ENCOUNTER_DESIGN.md §3.8</c>), split out from its DB-reading wrapper specifically so both fallback
/// behaviors are testable without a database: <c>fallback: true</c> (wild/Elite/Boss — theme wins if the
/// floor empties the pool) and <c>fallback: false</c> (draft — declines rather than falling back).
/// </summary>
public class EncounterFactoryFilterByMinLevelTests
{
    private static readonly IEvolutionRules Rules = Gen1EvolutionRules.Instance;

    private static PokemonSpecies Species(int id) => new() { Id = id };

    private static PokemonEvolution Level(int from, int to, int threshold) =>
        new()
        {
            FromSpeciesId = from,
            ToSpeciesId = to,
            Trigger = EvolutionTrigger.Level,
            LevelThreshold = threshold,
        };

    [Fact]
    public void SomeSpeciesEligible_ReturnsOnlyThoseAtOrBelowTheFloor()
    {
        // Species 2 (Kakuna, floor 7) is eligible at level 10; species 3 (Beedrill, floor 10) is not at 9.
        var pool = new List<PokemonSpecies> { Species(1), Species(2), Species(3) };
        var edges = new[] { Level(1, 2, 7), Level(2, 3, 10) };

        var result = EncounterFactory.FilterByMinLevel(
            pool,
            level: 9,
            edges,
            Rules,
            fallback: true
        );

        Assert.Equal([1, 2], result.Select(s => s.Id));
    }

    [Fact]
    public void NoneEligible_FallbackTrue_ReturnsTheFullUnfilteredPool()
    {
        // Every species needs a higher level than the roll — the wild/Elite/Boss path prefers staying
        // on-theme (the caller has already narrowed `pool` to the biome) over enforcing the floor.
        var pool = new List<PokemonSpecies> { Species(2), Species(3) };
        var edges = new[] { Level(1, 2, 7), Level(2, 3, 10) };

        var result = EncounterFactory.FilterByMinLevel(
            pool,
            level: 1,
            edges,
            Rules,
            fallback: true
        );

        Assert.Equal([2, 3], result.Select(s => s.Id));
    }

    [Fact]
    public void NoneEligible_FallbackFalse_ReturnsEmpty()
    {
        // The draft path: a drafted creature becomes a permanent party member, so an empty result here must
        // stay empty (the caller declines the offer) rather than handing back an under-leveled species.
        var pool = new List<PokemonSpecies> { Species(2), Species(3) };
        var edges = new[] { Level(1, 2, 7), Level(2, 3, 10) };

        var result = EncounterFactory.FilterByMinLevel(
            pool,
            level: 1,
            edges,
            Rules,
            fallback: false
        );

        Assert.Empty(result);
    }
}
