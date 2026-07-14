using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The boss-catch <em>build</em> path (<see cref="EncounterFactory.BuildBossCatchSupplier"/>) against the live
/// databases — the production seam the delegate-supplier plumbing tests (<c>RunDirectorAcquisitionTests</c>) stub
/// out and the policy tests (<c>BossCatchCalculatorTests</c>) don't reach. Pins the channel's headline invariant
/// through the DB: when the catch fires, the offered creature is a fresh full-HP copy of the defeated boss's
/// species at the boss's level; when the roll misses, nothing is offered.
/// <para>Runs against the live databases (the production path), so it requires them populated — run
/// <c>PokeApiConnector</c> on a fresh checkout.</para>
/// </summary>
public class EncounterFactoryBossCatchTests
{
    private static EncounterFactory BuildFactory() =>
        new(
            new LiveBossCatchDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveBossCatchDbContextFactory<MovesDbContext>(() => new MovesDbContext()),
            new LiveBossCatchDbContextFactory<ItemsDbContext>(() => new ItemsDbContext())
        );

    private const int Bulbasaur = 1;
    private const int Gyarados = 130; // a boss-worthy species distinct from the player

    // A source whose every draw is 0 — makes BossCatchCalculator's n% roll pass (0 < CatchPercent) so the catch
    // fires deterministically, and drives the creature build (DVs / moves) reproducibly.
    private sealed class AlwaysZero : IRandomSource
    {
        public int Next(int maxExclusive) => 0;

        public int Next(int minInclusive, int maxExclusive) => minInclusive;

        public double NextDouble() => 0.0;
    }

    // A source whose every draw is 99 — makes the n% roll miss (99 >= CatchPercent) so no catch is offered.
    private sealed class AlwaysHigh : IRandomSource
    {
        public int Next(int maxExclusive) => 99;

        public int Next(int minInclusive, int maxExclusive) => maxExclusive - 1;

        public double NextDouble() => 0.99;
    }

    [Fact]
    public async Task BuildBossCatchSupplier_WhenRollPasses_BuildsAFullHpCopyOfTheBossSpecies()
    {
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(1));
        Assert.NotNull(setup);

        // Stand in for the defeated boss: species Gyarados at level 42 (its actual HP is irrelevant — the catch
        // builds a fresh copy, so it must arrive at full HP regardless of the fainted battle instance).
        var boss = new Creature("GYARADOS") { Level = 42, SpeciesId = Gyarados };

        var catcher = factory.BuildBossCatchSupplier(setup!.AllMoves);
        var offered = await catcher(new BossCatchContext(boss), new AlwaysZero());

        Assert.NotNull(offered);
        Assert.Equal(Gyarados, offered!.SpeciesId); // the boss you beat, not some other species
        Assert.Equal(42, offered.Level); // built at the boss's own level
        Assert.Equal(offered.Attributes.MaxHP, offered.Attributes.HP); // a fresh caught copy arrives at full HP
        Assert.NotEmpty(offered.MoveSet); // built with a usable moveset
    }

    [Fact]
    public async Task BuildBossCatchSupplier_WhenRollMisses_OffersNothing()
    {
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(1));
        Assert.NotNull(setup);

        var boss = new Creature("GYARADOS") { Level = 42, SpeciesId = Gyarados };

        var catcher = factory.BuildBossCatchSupplier(setup!.AllMoves);
        var offered = await catcher(new BossCatchContext(boss), new AlwaysHigh());

        Assert.Null(offered); // the n% roll missed → no catch, and no DB build attempted
    }
}

/// <summary>Test factory over the live SQLite DBs (mirrors the production composition). File-scoped so it lives
/// alongside the tests that use it (a sibling of <c>EncounterFactoryDraftTests</c>' identical helper).</summary>
file sealed class LiveBossCatchDbContextFactory<TContext>(Func<TContext> create)
    : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
