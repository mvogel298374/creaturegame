using creaturegame.Combat;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Generations;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The pool-exclusion invariant behind <c>ENCOUNTER_DESIGN.md §3.8</c>, against the live databases and
/// today's Kanto roster: no <see cref="EncounterFactory.CreateEnemyAsync"/> result observed here falls below
/// its own evolution-chain floor (<see cref="EvolutionMinLevel"/>), with or without a biome theme. This is
/// <b>not</b> a structural guarantee — the biome-then-level filter order pins the *crash* case, but if a
/// biome's themed pool ever has zero floor-eligible species, the level filter's fallback deliberately reverts
/// to the full themed pool and can return a sub-floor species (an accepted, currently-unreachable residual,
/// §3.8). These tests only confirm the invariant holds for the biomes/seeds they exercise, not that it can
/// never be violated.
/// <para>Runs against the live databases (the production path) — run <c>PokeApiConnector</c> on a fresh
/// checkout.</para>
/// </summary>
public class EncounterFactoryMinLevelTests
{
    private static EncounterFactory BuildFactory() =>
        new(
            new LiveDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveDbContextFactory<MovesDbContext>(() => new MovesDbContext()),
            new LiveDbContextFactory<ItemsDbContext>(() => new ItemsDbContext())
        );

    private const int Bulbasaur = 1;

    private static async Task<List<PokemonEvolution>> LoadEdgesAsync()
    {
        await using var pokemonCtx = new PokemonDbContext();
        return await pokemonCtx
            .Evolutions.AsNoTracking()
            .Where(e => e.Generation == 1)
            .ToListAsync();
    }

    [Fact]
    public async Task CreateEnemyAsync_NeverReturnsASpeciesBelowItsEvolutionFloor_NoBiome()
    {
        var factory = BuildFactory();
        // A low-level lead so the level formula's band sits well under the higher evolution floors
        // (Charizard's 36, the trade lines' 37) — the case that used to slip through.
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            10,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);
        var edges = await LoadEdgesAsync();

        for (int seed = 1; seed <= 30; seed++)
        {
            var enemy = await factory.CreateEnemyAsync(
                setup!.Player,
                setup.AllMoves,
                Gen1Profile.Instance,
                new SeededRandomSource(seed),
                biome: null,
                depth: seed % 6
            );
            int floor = EvolutionMinLevel.Compute(
                enemy.SpeciesId,
                edges,
                Gen1Profile.Instance.EvolutionRules
            );
            Assert.True(
                floor <= enemy.Level,
                $"seed {seed}: species {enemy.SpeciesId} at level {enemy.Level} is below its evolution floor {floor}"
            );
        }
    }

    [Fact]
    public async Task CreateEnemyAsync_NeverReturnsASpeciesBelowItsEvolutionFloor_AcrossEveryBiome()
    {
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            10,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);
        var edges = await LoadEdgesAsync();

        foreach (var biome in Gen1Profile.Instance.BiomeRoster)
        {
            for (int seed = 1; seed <= 5; seed++)
            {
                var enemy = await factory.CreateEnemyAsync(
                    setup!.Player,
                    setup.AllMoves,
                    Gen1Profile.Instance,
                    new SeededRandomSource(seed),
                    biome,
                    depth: 2
                );
                int floor = EvolutionMinLevel.Compute(
                    enemy.SpeciesId,
                    edges,
                    Gen1Profile.Instance.EvolutionRules
                );
                Assert.True(
                    floor <= enemy.Level,
                    $"{biome.Name}, seed {seed}: species {enemy.SpeciesId} at level {enemy.Level} is below its evolution floor {floor}"
                );
            }
        }
    }
}
