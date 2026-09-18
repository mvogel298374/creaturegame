using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Generations;
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
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        int[] fought = [16, 19]; // Pidgey, Rattata — a two-species fought pool
        var draft = factory.BuildDraftSupplier(setup!.AllMoves, Gen1Profile.Instance);
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

    private const int Charizard = 6; // needs Charmeleon → Charizard at level 36 (ENCOUNTER_DESIGN.md §3.8)

    [Fact]
    public async Task BuildDraftSupplier_DeclinesWhenTheOnlyFoughtSpeciesIsBelowItsEvolutionFloor()
    {
        var factory = BuildFactory();
        // A low-level lead: ScaleWildLevel(10, depth: 0, AlwaysZero) rolls the band's floor, 5 — well below
        // the 36 Charizard needs, so a naive fallback-to-unfiltered-pool would hand it back anyway.
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            10,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        var draft = factory.BuildDraftSupplier(setup!.AllMoves, Gen1Profile.Instance);
        var offered = await draft(
            new DraftContext(
                setup.Player,
                Depth: 0,
                Biome: null,
                FoughtSpecies: [Charizard], // the only candidate, and it's unreachable at the rolled level
                BattlesWon: DraftCalculator.CadenceEveryNWins
            ),
            new AlwaysZero()
        );

        // A drafted creature becomes a permanent party member, so this must decline rather than offer an
        // under-leveled Charizard (ENCOUNTER_DESIGN.md §3.8) — never falls back to the unfiltered pool.
        Assert.Null(offered);
    }

    // Floors every evolution edge at 99 regardless of trigger — deliberately unlike Gen1EvolutionRules, so a
    // draft offer's outcome flips only if EncounterFactory actually consults the profile's IEvolutionRules
    // rather than a hardcoded Gen1EvolutionRules reference.
    private sealed class EveryEdgeFloorsAt99 : IEvolutionRules
    {
        public EvolutionResult? CheckEvolution(
            Creature creature,
            EvolutionContext context,
            IReadOnlyList<PokemonEvolution> edges
        ) => null;

        public int MinLevelFor(PokemonEvolution edge) => 99;
    }

    private const int Kakuna = 14; // Weedle → Kakuna at level 7 under Gen1EvolutionRules

    [Fact]
    public async Task BuildDraftSupplier_ObservesTheProfilesEvolutionRules_NotAHardcodedGen1Reference()
    {
        var factory = BuildFactory();
        // Lead level 50: ScaleWildLevel(50, depth: 0, AlwaysZero) rolls the band's floor, 25 — above Kakuna's
        // real Gen 1 floor (7) so the Gen 1 profile offers it, but below the stub profile's floor (99) so a
        // profile that actually threads through must decline instead.
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        var context = new DraftContext(
            setup!.Player,
            Depth: 0,
            Biome: null,
            FoughtSpecies: [Kakuna],
            BattlesWon: DraftCalculator.CadenceEveryNWins
        );

        var underGen1 = await factory.BuildDraftSupplier(setup.AllMoves, Gen1Profile.Instance)(
            context,
            new AlwaysZero()
        );
        Assert.NotNull(underGen1);
        Assert.Equal(Kakuna, underGen1!.SpeciesId);

        var stubProfile = Gen1Profile.Instance with { EvolutionRules = new EveryEdgeFloorsAt99() };
        var underStub = await factory.BuildDraftSupplier(setup.AllMoves, stubProfile)(
            context,
            new AlwaysZero()
        );
        Assert.Null(underStub); // would still offer Kakuna if the seam swap were silently ignored
    }

    [Fact]
    public async Task BuildDraftSupplier_EmptyFoughtPool_NeverOffers()
    {
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        var draft = factory.BuildDraftSupplier(setup!.AllMoves, Gen1Profile.Instance);
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
