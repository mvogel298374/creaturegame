using creaturegame.Combat;
using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The themed-draft <em>build</em> path (<see cref="EncounterFactory.BuildDraftSupplier"/>) against the live
/// databases — the production seam the delegate-supplier plumbing tests (<c>RunDirectorAcquisitionTests</c>)
/// stub out and the policy tests (<c>DraftCalculatorTests</c>) don't reach. Pins the feature's headline safety
/// invariant end-to-end through the DB: an offered creature is <em>only ever</em> a species from the fought-only
/// pool (ENCOUNTER_DESIGN.md §4), and an empty pool never offers.
/// <para>Runs against the live databases (the production path), so it requires them populated — run
/// <c>PokeApiConnector</c> on a fresh checkout.</para>
/// </summary>
public class EncounterFactoryDraftTests
{
    private static EncounterFactory BuildFactory() =>
        new(
            new LiveDraftDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveDraftDbContextFactory<MovesDbContext>(() => new MovesDbContext()),
            new LiveDraftDbContextFactory<ItemsDbContext>(() => new ItemsDbContext())
        );

    private const int Bulbasaur = 1;

    // A source whose every draw is 0 — makes DraftCalculator's n% roll pass (0 < OfferPercent) so the offer
    // fires deterministically, and drives the creature build (species pick / DVs / moves) reproducibly.
    private sealed class AlwaysZero : IRandomSource
    {
        public int Next(int maxExclusive) => 0;

        public int Next(int minInclusive, int maxExclusive) => minInclusive;

        public double NextDouble() => 0.0;
    }

    [Fact]
    public async Task BuildDraftSupplier_OffersOnlyASpeciesFromTheFoughtPool()
    {
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(1));
        Assert.NotNull(setup);

        int[] fought = [16, 19]; // Pidgey, Rattata — a two-species fought pool
        var draft = factory.BuildDraftSupplier(setup!.AllMoves);
        var offered = await draft(
            new DraftContext(
                setup.Player,
                Depth: 3,
                Biome: null,
                FoughtSpecies: fought,
                BattlesWon: DraftCalculator.CadenceEveryNWins // a cadence win
            ),
            new AlwaysZero() // roll passes → the offer fires
        );

        Assert.NotNull(offered);
        Assert.Contains(offered!.SpeciesId, fought); // NEVER an un-fought species — the §4 guardrail
        Assert.True(offered.Level > 0);
        Assert.Equal(offered.Attributes.MaxHP, offered.Attributes.HP); // a fresh draftee arrives at full HP
        Assert.NotEmpty(offered.MoveSet); // built with a usable moveset
    }

    [Fact]
    public async Task BuildDraftSupplier_EmptyFoughtPool_NeverOffers()
    {
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(Bulbasaur, 50, new SeededRandomSource(1));
        Assert.NotNull(setup);

        var draft = factory.BuildDraftSupplier(setup!.AllMoves);
        var offered = await draft(
            new DraftContext(
                setup.Player,
                Depth: 3,
                Biome: null,
                FoughtSpecies: [], // nothing fought this biome → never a dead offer
                BattlesWon: DraftCalculator.CadenceEveryNWins
            ),
            new AlwaysZero()
        );

        Assert.Null(offered);
    }
}

/// <summary>Test factory over the live SQLite DBs (mirrors the production composition). File-scoped so it lives
/// alongside the tests that use it (a sibling of <c>RunSetupBiomeTests</c>' identical helper).</summary>
file sealed class LiveDraftDbContextFactory<TContext>(Func<TContext> create)
    : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
}
