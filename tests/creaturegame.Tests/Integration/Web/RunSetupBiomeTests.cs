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
    public async Task CreatePlayerSetup_DrawsASeededPerRunBiomeMap()
    {
        var setup = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(1));

        Assert.NotNull(setup);
        // The run map is a seeded subset of the playable set (ENCOUNTER_DESIGN.md §2.1) — RunBiomeMapSize biomes
        // (the full Kanto roster is non-empty against the live Wild pool, §2.3, so the subset is exactly that
        // size), all real Kanto biomes, no duplicates.
        Assert.Equal(EncounterFactory.RunBiomeMapSize, setup!.PlayableBiomes.Count);
        Assert.All(setup.PlayableBiomes, b => Assert.Equal(Region.Kanto, b.Region));
        Assert.All(setup.PlayableBiomes, b => Assert.Contains(b, Biomes.Kanto));
        Assert.Equal(
            setup.PlayableBiomes.Count,
            setup.PlayableBiomes.Select(b => b.Id).Distinct().Count()
        );
    }

    [Fact]
    public async Task CreatePlayerSetup_BiomeMap_IsReproducibleFromSeed()
    {
        var a = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(42));
        var b = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(42));

        Assert.Equal(a!.PlayableBiomes.Select(x => x.Id), b!.PlayableBiomes.Select(x => x.Id)); // same seed ⇒ same map
    }

    [Fact]
    public async Task FirstEncounter_AtPlayerLevel25_NeverDropsBelowTheDepthZeroBand()
    {
        // Repro guard for the "level-9 foe vs a level-25 player on the first battle" report. Builds the player
        // the way the run does (CreatePlayerSetupAsync) and the first foe the way the director does at depth 0
        // (default Medium tier), over many seeds. At player 25 the [50%,80%] band is [12,20] — the foe must
        // never fall below 12, so a single-digit level is impossible from this path.
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(Bulbasaur, 25, new SeededRandomSource(1));

        Assert.NotNull(setup);
        Assert.Equal(25, setup!.Player.Level); // the player genuinely is level 25 out of setup

        for (int seed = 0; seed < 60; seed++)
        {
            var enemy = await factory.CreateEnemyAsync(
                setup.Player,
                setup.AllMoves,
                new SeededRandomSource(seed),
                depth: 0
            );
            Assert.InRange(enemy.Level, 12, 20);
        }
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
