using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// Proves the web composition root's per-run seed actually makes a run reproducible: the same seed, threaded
/// through <see cref="EncounterFactory"/> the way <c>GameController</c>/<c>GameSessionManager</c> do, must
/// reproduce identical creature *construction* — DVs (<see cref="IStatCalculator"/>), the picked species and
/// level, and the selected moveset. This is the wiring guard for Architecture Review #3: the seams were
/// already seedable; the leak was the run layer defaulting them to the global RNG.
/// <para>Runs against the live databases (the real production path), so it requires them populated — run
/// <c>PokeApiConnector</c> on a fresh checkout.</para>
/// </summary>
public class RunSeedReproducibilityTests
{
    private static EncounterFactory BuildFactory() =>
        new(
            new LiveDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveDbContextFactory<MovesDbContext>(() => new MovesDbContext()),
            new LiveDbContextFactory<ItemsDbContext>(() => new ItemsDbContext())
        );

    private const int Bulbasaur = 1;

    [Fact]
    public async Task SameSeed_BuildsIdenticalPlayer()
    {
        var factory = BuildFactory();

        var a = await factory.CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(42));
        var b = await factory.CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(42));

        Assert.NotNull(a);
        Assert.NotNull(b);
        AssertSameCreature(a!.Player, b!.Player);
    }

    [Fact]
    public async Task CreatePlayerSetup_SeedsTheRunBagFromTheItemCatalog()
    {
        var setup = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(1));

        Assert.NotNull(setup);
        Assert.NotEmpty(setup!.AllItems);
        // Every catalog item is stocked in the bag with a positive quantity (the generous test loadout).
        Assert.All(setup.AllItems, i => Assert.True(setup.Bag.Count(i.Id) > 0));
        // A known battle item is present (Potion, id 17).
        Assert.True(setup.Bag.Count(17) > 0);
    }

    [Fact]
    public async Task SameSeed_BuildsIdenticalEnemy()
    {
        var factory = BuildFactory();
        // A fixed player drives the enemy's BST/level scaling; build it once and reuse for both encounters.
        var setup = await factory.CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(7));
        Assert.NotNull(setup);

        var e1 = await factory.CreateEnemyAsync(
            setup!.Player,
            setup.AllMoves,
            new SeededRandomSource(1234)
        );
        var e2 = await factory.CreateEnemyAsync(
            setup.Player,
            setup.AllMoves,
            new SeededRandomSource(1234)
        );

        Assert.Equal(e1.SpeciesId, e2.SpeciesId); // same species pick (PickByBst)
        Assert.Equal(e1.Level, e2.Level); // same level (ScaleWildLevel)
        AssertSameCreature(e1, e2);

        // Depth > 0 takes a distinct RNG-draw path: ScaleTargetBst shifts targetBst, changing the candidate
        // set PickByBst indexes into, and ScaleWildLevel lifts the band. It must still be seed-reproducible.
        var d1 = await factory.CreateEnemyAsync(
            setup.Player,
            setup.AllMoves,
            new SeededRandomSource(1234),
            depth: 5
        );
        var d2 = await factory.CreateEnemyAsync(
            setup.Player,
            setup.AllMoves,
            new SeededRandomSource(1234),
            depth: 5
        );
        Assert.Equal(d1.SpeciesId, d2.SpeciesId);
        Assert.Equal(d1.Level, d2.Level);
        AssertSameCreature(d1, d2);
    }

    [Fact]
    public async Task CreateEnemy_DrawsOnlyFromWildAvailableSpecies()
    {
        // Pins the Phase 1 Wild filter: enemies never come from non-wild species (legendaries/statics/gifts/
        // fossils). Without the filter, in-band non-wild species (fossils, Eeveelutions, Hitmons, …) would
        // surface — this would catch a regression that the determinism test alone (full-dex fallback stays
        // deterministic too) would not.
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(3));
        Assert.NotNull(setup);

        await using var ctx = new PokemonDbContext();
        var wild = ctx
            .GameAvailability.AsNoTracking()
            .Where(a => a.AvailabilityType == "Wild")
            .Select(a => a.SpeciesId)
            .Distinct()
            .ToHashSet();
        Assert.NotEmpty(wild); // guard: the live DB carries availability data, so the filter is exercised

        for (int i = 0; i < 100; i++)
        {
            var enemy = await factory.CreateEnemyAsync(
                setup!.Player,
                setup.AllMoves,
                new SeededRandomSource(i)
            );
            Assert.Contains(enemy.SpeciesId, wild);
        }
    }

    private static void AssertSameCreature(Creature x, Creature y)
    {
        Assert.Equal(x.SpeciesId, y.SpeciesId);
        Assert.Equal(x.Level, y.Level);
        // DVs come from the seeded IStatCalculator — the part that was previously unseeded at the run layer.
        Assert.Equal(
            (x.DvHP, x.DvAttack, x.DvDefense, x.DvSpecial, x.DvSpeed),
            (y.DvHP, y.DvAttack, y.DvDefense, y.DvSpecial, y.DvSpeed)
        );
        // Moveset (ids, in order) — the seeded WeightedSmart / fallback selection.
        Assert.Equal(
            x.MoveSet.Select(m => m.Base.Id).ToArray(),
            y.MoveSet.Select(m => m.Base.Id).ToArray()
        );
    }
}

/// <summary>
/// Minimal <see cref="IDbContextFactory{TContext}"/> over a context constructor, so an
/// <see cref="EncounterFactory"/> can be built in tests against the live databases (the parameterless
/// context ctors resolve them). <c>CreateDbContextAsync</c> uses the interface's default implementation.
/// </summary>
file sealed class LiveDbContextFactory<TContext>(Func<TContext> create)
    : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
