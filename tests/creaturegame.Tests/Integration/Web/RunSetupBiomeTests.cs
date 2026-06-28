using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// Phase 3b-2 run setup: <see cref="EncounterFactory.CreatePlayerSetupAsync"/> computes the run's playable
/// biome map (the region's biomes that can generate against the wild-available pool), which
/// <c>GameSessionManager</c> threads into the <see cref="RunDirector"/> to activate biome mode. Empty biomes
/// never appear, so every biome offered on the map is guaranteed to have an encounter.
/// <para>Runs against the live databases (the production path), so it requires them populated — run
/// <c>PokeApiConnector</c> on a fresh checkout.</para>
/// </summary>
public class RunSetupBiomeTests
{
    private static EncounterFactory BuildFactory() =>
        new(
            new LiveDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveDbContextFactory<MovesDbContext>(() => new MovesDbContext()),
            new LiveDbContextFactory<ItemsDbContext>(() => new ItemsDbContext())
        );

    private const int Bulbasaur = 1;

    [Fact]
    public async Task CreatePlayerSetup_ComputesTheFullKantoBiomeMap()
    {
        var setup = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(1));

        Assert.NotNull(setup);
        // Every authored Kanto biome is non-empty against the live Wild pool (ENCOUNTER_DESIGN.md §2.3 verified
        // the thinnest at 5), so the whole roster is playable — none dropped at map-generation time.
        Assert.Equal(Biomes.Kanto.Count, setup!.PlayableBiomes.Count);
        Assert.All(setup.PlayableBiomes, b => Assert.Equal(Region.Kanto, b.Region));
        // A known biome is present, with its authored theme intact (the cards render these badges).
        var marsh = Assert.Single(setup.PlayableBiomes, b => b.Id == "phantom-marsh");
        Assert.Equal(new[] { DamageType.Ghost, DamageType.Poison }, marsh.Types.ToArray());
    }

    [Fact]
    public async Task CreatePlayerSetup_PlayableBiomes_OnlyContainNonEmptyThemes()
    {
        var setup = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(2));

        Assert.NotNull(setup);
        Assert.NotEmpty(setup!.PlayableBiomes);
        // Each playable biome must have at least one wild-available on-theme species — otherwise PickByBst would
        // starve on its themed pool. Re-derive the wild pool the way the factory does and assert membership.
        await using var ctx = new PokemonDbContext();
        var allSpecies = ctx.Species.ToList();
        var wildSet = ctx
            .GameAvailability.Where(a => a.AvailabilityType == "Wild")
            .Select(a => a.SpeciesId)
            .Distinct()
            .ToHashSet();
        var wildPool =
            wildSet.Count > 0 ? allSpecies.Where(s => wildSet.Contains(s.Id)).ToList() : allSpecies;

        Assert.All(setup.PlayableBiomes, b => Assert.True(b.HasAnyIn(wildPool)));
    }
}

/// <summary>Test factory over the live SQLite DBs — mirrors the production composition (parameterless context
/// ctors resolve them). File-scoped, so it lives alongside the tests that use it.</summary>
file sealed class LiveDbContextFactory<TContext>(Func<TContext> create)
    : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
