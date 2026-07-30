using System.Linq.Expressions;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.DB;
using creaturegame.Generations;
using creaturegame.Items;
using creaturegame.Tests.Unit;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The falsification legs (<c>docs/GENERATION_PROFILE.md</c> §3) for everything <see cref="EncounterFactory"/>
/// takes off the run's profile rather than from the hardcoded Gen 1 sources it used to.
/// <para>Three separate leaks are covered, because they failed in three different ways:</para>
/// <list type="bullet">
/// <item><b>The stat seam</b> (Stage 1b) — <c>BuildCreature</c> constructed its own
/// <c>new Gen1StatCalculator(rng)</c>, so a profile could supply any <c>BuildStatCalculator</c> it liked and be
/// silently ignored.</item>
/// <item><b>The generation as a data filter</b> (Stage 1b) — a <c>private const int ActiveGeneration = 1</c>
/// drove six learnset/evolution queries, so a non-Gen-1 run would still have been served Gen 1 rows.</item>
/// <item><b>The content scope</b> (Stage 2b) — the species, move and item catalogs were read unfiltered, so the
/// run's content was Gen 1 by nothing more than what the databases happen to hold. Its probes are the sharpest
/// case of why a second profile is needed: Gen 1's scope is an <i>identity function</i>, so a call site that
/// skipped it entirely is invisible from inside Gen 1 — hence one probe per catalog read, not one per
/// method.</item>
/// </list>
/// <para>All three are the shape <c>§4.2</c> warns about: before each stage, deleting the thread would have left
/// the whole suite green, because Gen 1 was the answer either way. These tests are only meaningful because a
/// <i>second</i> profile exists to make "we got Gen 1" a wrong answer — see <see cref="TestAltProfile"/>, which
/// is not Gen 2 and asserts nothing about fidelity.</para>
/// <para>Runs against the live databases (the real production path) — run <c>PokeApiConnector</c> on a fresh
/// checkout.</para>
/// </summary>
public class EncounterFactoryGenerationProfileTests
{
    private static EncounterFactory BuildFactory() =>
        new(
            new LiveDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveDbContextFactory<MovesDbContext>(() => new MovesDbContext()),
            new LiveDbContextFactory<ItemsDbContext>(() => new ItemsDbContext())
        );

    private const int Bulbasaur = 1;

    /// <summary>Every draw is 0 — passes the draft/boss-catch policy gates deterministically so a creature is
    /// actually built. The alt stat calculator ignores its rng entirely, so this only has to get past the
    /// gates.</summary>
    private sealed class AlwaysZero : IRandomSource
    {
        public int Next(int maxExclusive) => 0;

        public int Next(int minInclusive, int maxExclusive) => minInclusive;

        public double NextDouble() => 0.0;
    }

    // ── The IStatCalculator seam ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePlayerSetup_TakesItsStatCalculatorFromTheProfile_NotAHardcodedGen1One()
    {
        var setup = await BuildFactory()
            .CreatePlayerSetupAsync(
                Bulbasaur,
                50,
                TestAltProfile.Instance,
                new SeededRandomSource(1)
            );

        Assert.NotNull(setup);
        // The alt calculator stamps a sentinel outside Gen 1's 0–15 DV range on every stat. Reaching it proves
        // the profile's factory ran; a leaked `new Gen1StatCalculator(rng)` could not produce these values.
        AssertBuiltByTheAltStatSeam(setup!.Player);
    }

    [Fact]
    public async Task CreateEnemy_TakesItsStatCalculatorFromTheProfile_NotAHardcodedGen1One()
    {
        var factory = BuildFactory();
        // The player is built under Gen 1 on purpose: only the ENEMY's build path is under test here, and it
        // must follow the profile it is handed rather than anything about the creature it is scaled against.
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        var enemy = await factory.CreateEnemyAsync(
            setup!.Player,
            setup.AllMoves,
            TestAltProfile.Instance,
            new SeededRandomSource(1234)
        );

        AssertBuiltByTheAltStatSeam(enemy);
    }

    [Fact]
    public async Task BuildDraftSupplier_TakesItsStatCalculatorFromTheProfile_NotAHardcodedGen1One()
    {
        // The third BuildCreature caller. Covered separately from the player/enemy pair because it is reached
        // through a supplier closure rather than a direct call — a re-leak here would be invisible to both.
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        var draft = factory.BuildDraftSupplier(setup!.AllMoves, TestAltProfile.Instance);
        var offered = await draft(
            new DraftContext(
                setup.Player,
                Depth: 3,
                Biome: null,
                FoughtSpecies: [16, 19], // Pidgey, Rattata — a non-empty fought pool so the offer can fire
                BattlesWon: DraftCalculator.CadenceEveryNWins // a cadence win
            ),
            new AlwaysZero() // the policy roll passes → a creature is actually built
        );

        Assert.NotNull(offered);
        AssertBuiltByTheAltStatSeam(offered!);
    }

    [Fact]
    public async Task BuildBossCatchSupplier_TakesItsStatCalculatorFromTheProfile_NotAHardcodedGen1One()
    {
        // The fourth and last BuildCreature caller — same reasoning as the draft probe above.
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        // The boss species must sit INSIDE TestAltProfile's content scope (Stage 2b), or the catch is refused
        // before any creature is built and this probe passes vacuously — it is the stat seam under test here,
        // not the scope. The scope's own boss-catch probe below uses an out-of-scope species deliberately.
        var boss = new creaturegame.Creatures.Creature("RATICATE")
        {
            Level = 42,
            SpeciesId = Raticate,
        };
        var catcher = factory.BuildBossCatchSupplier(setup!.AllMoves, TestAltProfile.Instance);
        var offered = await catcher(new BossCatchContext(boss), new AlwaysZero());

        Assert.NotNull(offered);
        AssertBuiltByTheAltStatSeam(offered!);
    }

    [Fact]
    public async Task UnderGen1_TheStatSeamStillRollsRealDvs_SoTheAltProbeIsNotVacuous()
    {
        // The control. Without this, the two tests above would still pass if BuildCreature had been broken to
        // stamp sentinels unconditionally — they'd be asserting a constant, not a thread.
        var setup = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, Gen1Profile.Instance, new SeededRandomSource(1));

        Assert.NotNull(setup);
        var p = setup!.Player;
        Assert.All(
            new[] { p.DvHP, p.DvAttack, p.DvDefense, p.DvSpecial, p.DvSpeed },
            dv => Assert.InRange(dv, 0, 15) // Gen 1's real DV range
        );
    }

    // ── The generation as a data filter ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePlayerSetup_ReadsLearnsetRowsForTheProfilesGeneration_NotAHardcodedOne()
    {
        // A profile that claims a generation the database has no rows for. The learnset must come back EMPTY.
        // Under the old `const int ActiveGeneration = 1` this returned Bulbasaur's full Gen 1 learnset no matter
        // what generation the run had selected — the second source of truth this stage removed.
        var setup = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, UnseededGeneration(), new SeededRandomSource(1));

        Assert.NotNull(setup);
        Assert.Empty(setup!.Player.Learnset);
    }

    [Fact]
    public async Task CreatePlayerSetup_UnderGen1_StillResolvesARealLearnset()
    {
        // The control for the filter probe: Gen 1 must still find its rows, so the assertion above is evidence
        // of filtering and not of a query that returns nothing for everyone.
        var setup = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, Gen1Profile.Instance, new SeededRandomSource(1));

        Assert.NotNull(setup);
        Assert.NotEmpty(setup!.Player.Learnset);
    }

    [Fact]
    public async Task ResolvePlayerEvolution_ReadsEvolutionEdgesForTheProfilesGeneration()
    {
        var factory = BuildFactory();
        // Level 50 Bulbasaur has a live Gen 1 evolution edge — verified by the control below, so an empty result
        // under the unseeded generation can only be the generation filter.
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        Assert.NotNull(
            await factory.ResolvePlayerEvolutionAsync(
                setup!.Player,
                setup.AllMoves,
                Gen1Profile.Instance
            )
        );
        Assert.Null(
            await factory.ResolvePlayerEvolutionAsync(
                setup.Player,
                setup.AllMoves,
                UnseededGeneration()
            )
        );
    }

    // ── The content scope (Stage 2b) ───────────────────────────────────────────────────────────────────

    // Every catalog read in EncounterFactory gets its own probe below. That is not over-testing: Gen 1's scope
    // is an IDENTITY function, so a call site that skipped the scope entirely would behave identically to one
    // that used it — §4.2's silent-fallback hazard, one layer out. Only a scope that actually removes rows can
    // tell the two apart, and only per site, because each site queries independently.
    // TestAltProfile's scope admits ids <= MaxContentId (20) across species, moves and items alike.

    private const int Pikachu = 25; // out of the alt scope
    private const int Ivysaur = 2; // in it, and evolves into Venusaur (3)
    private const int Venusaur = 3;
    private const int Gyarados = 130; // out of it
    private const int Raticate = 20; // the top of the alt scope, so `<=` not `<`

    [Fact]
    public async Task CreatePlayerSetup_DrawsItsSpeciesThroughTheProfilesContentScope()
    {
        var factory = BuildFactory();

        // Out of scope → unknown, exactly as a nonexistent id would be. Not an error path worth softening: a
        // starter the run's generation does not have is not a startable run.
        Assert.Null(
            await factory.CreatePlayerSetupAsync(
                Pikachu,
                50,
                TestAltProfile.Instance,
                new SeededRandomSource(1)
            )
        );
        // …while an in-scope species still builds. Without this the assertion above would also pass if the
        // scope had broken species lookup outright.
        Assert.NotNull(
            await factory.CreatePlayerSetupAsync(
                Raticate,
                50,
                TestAltProfile.Instance,
                new SeededRandomSource(1)
            )
        );
        // The control: Gen 1's identity scope admits the species the alt one rejected.
        Assert.NotNull(
            await factory.CreatePlayerSetupAsync(
                Pikachu,
                50,
                Gen1Profile.Instance,
                new SeededRandomSource(1)
            )
        );
    }

    [Fact]
    public async Task CreatePlayerSetup_DrawsTheRunsMovePoolThroughTheProfilesContentScope()
    {
        // The move pool is loaded once and threaded into every creature the run builds, so this single read is
        // the one that scopes every moveset in the run.
        var alt = await BuildFactory()
            .CreatePlayerSetupAsync(
                Bulbasaur,
                50,
                TestAltProfile.Instance,
                new SeededRandomSource(1)
            );
        var gen1 = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, Gen1Profile.Instance, new SeededRandomSource(1));

        Assert.NotNull(alt);
        Assert.NotEmpty(alt!.AllMoves); // not vacuous: the scope narrows the pool, it doesn't empty it
        Assert.All(alt.AllMoves, m => Assert.True(m.Id <= TestAltProfile.MaxContentId));
        // The control: the real pool reaches well past the alt ceiling, so the assertion above is evidence of
        // filtering rather than of a move table that happens to be small.
        Assert.NotNull(gen1);
        Assert.Contains(gen1!.AllMoves, m => m.Id > TestAltProfile.MaxContentId);
    }

    [Fact]
    public async Task CreatePlayerSetup_DrawsTheRunsItemCatalogThroughTheProfilesContentScope()
    {
        // Same shape as the move pool, and the same reach: this one load backs the starting bag, the shop and
        // every reward roll for the whole run.
        var alt = await BuildFactory()
            .CreatePlayerSetupAsync(
                Bulbasaur,
                50,
                TestAltProfile.Instance,
                new SeededRandomSource(1)
            );
        var gen1 = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, Gen1Profile.Instance, new SeededRandomSource(1));

        Assert.NotNull(alt);
        Assert.NotEmpty(alt!.AllItems); // not vacuous: the scope narrows the catalog, it doesn't empty it
        Assert.All(alt.AllItems, i => Assert.True(i.Id <= TestAltProfile.MaxContentId));
        Assert.NotNull(gen1);
        Assert.Contains(gen1!.AllItems, i => i.Id > TestAltProfile.MaxContentId);
    }

    [Fact]
    public async Task CreatePlayerSetup_BuildsTheBiomeMapFromScopedSpecies_SoAGenerationGetsTheBiomesItCanFill()
    {
        // The map's playable set is "biomes with at least one on-theme species" (Biomes.Playable), so scoping the
        // species pool scopes the map: a generation gets the biomes its own content can fill. That is the
        // consequence this read needed the scope for, having previously been "the whole dex regardless of
        // generation" — and it says so in its own doc comment.
        //
        // Uses a tighter scope than TestAltProfile's, because that one is not restrictive enough to be observed
        // HERE: ids 1-20 still cover enough of Kanto's themes to leave more than RunBiomeMapSize biomes playable,
        // so the map comes back capped at 10 either way (measured, not assumed). Narrowing to the first five
        // species leaves seven — fewer than the cap, which is what makes the scope visible in the result.
        var narrow = Gen1Profile.Instance with
        {
            ContentScope = new SpeciesScope(s => s.Id <= 5),
        };
        var alt = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, narrow, new SeededRandomSource(1));
        var gen1 = await BuildFactory()
            .CreatePlayerSetupAsync(Bulbasaur, 50, Gen1Profile.Instance, new SeededRandomSource(1));

        Assert.NotNull(alt);
        Assert.NotNull(gen1);
        // Gen 1 fills the run map to its cap; the narrowed scope cannot, because too few biomes stay playable.
        Assert.Equal(EncounterFactory.RunBiomeMapSize, gen1!.PlayableBiomes.Count);
        Assert.True(
            alt!.PlayableBiomes.Count < EncounterFactory.RunBiomeMapSize,
            $"expected a narrower map under the narrowed scope, got {alt.PlayableBiomes.Count}"
        );
        Assert.NotEmpty(alt.PlayableBiomes); // a map that starved would pass the line above for the wrong reason
    }

    [Fact]
    public async Task CreateEnemy_DrawsItsSpeciesPoolThroughTheProfilesContentScope()
    {
        var factory = BuildFactory();
        // Built under Gen 1 on purpose — only the ENEMY read is under test, as in the stat-seam probes above.
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        // Several seeds: one encounter landing in scope by luck would prove nothing about a pool of ~150.
        var altIds = new List<int>();
        var gen1Ids = new List<int>();
        for (int seed = 1; seed <= 8; seed++)
        {
            altIds.Add(
                (
                    await factory.CreateEnemyAsync(
                        setup!.Player,
                        setup.AllMoves,
                        TestAltProfile.Instance,
                        new SeededRandomSource(seed)
                    )
                ).SpeciesId
            );
            gen1Ids.Add(
                (
                    await factory.CreateEnemyAsync(
                        setup.Player,
                        setup.AllMoves,
                        Gen1Profile.Instance,
                        new SeededRandomSource(seed)
                    )
                ).SpeciesId
            );
        }

        Assert.All(
            altIds,
            id => Assert.True(id <= TestAltProfile.MaxContentId, $"got species {id}")
        );
        // The control: the same seeds against the unscoped pool reach past the ceiling, so the assertion above
        // is the scope's doing and not a quirk of BST banding at level 50.
        Assert.Contains(gen1Ids, id => id > TestAltProfile.MaxContentId);
    }

    [Fact]
    public async Task BuildDraftSupplier_DrawsItsFoughtPoolThroughTheProfilesContentScope()
    {
        // The draft pool is the species you fought; scoping it out leaves nothing to offer. Reached through a
        // supplier closure rather than a direct call, so a re-leak here is invisible to every probe above.
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        // Both fought species sit outside the alt scope.
        var fought = new[] { Pikachu, 26 };

        Assert.Null(await Offer(TestAltProfile.Instance));
        Assert.NotNull(await Offer(Gen1Profile.Instance)); // the control

        async Task<creaturegame.Creatures.Creature?> Offer(GenerationProfile profile) =>
            await factory.BuildDraftSupplier(setup!.AllMoves, profile)(
                new DraftContext(
                    setup.Player,
                    Depth: 3,
                    Biome: null,
                    FoughtSpecies: fought,
                    BattlesWon: DraftCalculator.CadenceEveryNWins
                ),
                new AlwaysZero()
            );
    }

    [Fact]
    public async Task BuildBossCatchSupplier_DrawsTheCaughtSpeciesThroughTheProfilesContentScope()
    {
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            Bulbasaur,
            50,
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        var boss = new creaturegame.Creatures.Creature("GYARADOS")
        {
            Level = 42,
            SpeciesId = Gyarados,
        };

        Assert.Null(await Catch(TestAltProfile.Instance)); // out of scope → no catch, not a Gen 1 Gyarados
        Assert.NotNull(await Catch(Gen1Profile.Instance)); // the control

        async Task<creaturegame.Creatures.Creature?> Catch(GenerationProfile profile) =>
            await factory.BuildBossCatchSupplier(setup!.AllMoves, profile)(
                new BossCatchContext(boss),
                new AlwaysZero()
            );
    }

    [Fact]
    public async Task ResolvePlayerEvolution_DrawsTheEvolvedFormThroughTheProfilesContentScope()
    {
        // The eighth and last catalog read. TestAltProfile's ceiling cannot probe it — every Gen 1 evolution
        // line that starts under id 20 also ENDS under it — so this uses a scope built to exclude exactly the
        // one species the evolution resolves to. The read is arguably redundant (the edges are already
        // generation-filtered), but "no unscoped catalog read in this file" is only a rule if it is checked.
        var factory = BuildFactory();
        var setup = await factory.CreatePlayerSetupAsync(
            Ivysaur,
            50, // past the level-32 threshold, so an edge really does fire
            Gen1Profile.Instance,
            new SeededRandomSource(1)
        );
        Assert.NotNull(setup);

        // The control first: Gen 1 resolves Venusaur, so a null below can only be the scope.
        Assert.NotNull(
            await factory.ResolvePlayerEvolutionAsync(
                setup!.Player,
                setup.AllMoves,
                Gen1Profile.Instance
            )
        );
        Assert.Null(
            await factory.ResolvePlayerEvolutionAsync(
                setup.Player,
                setup.AllMoves,
                Gen1Profile.Instance with
                {
                    ContentScope = new SpeciesScope(s => s.Id != Venusaur),
                }
            )
        );
    }

    /// <summary>A content scope that narrows species by an arbitrary predicate and passes moves and items
    /// through — the sharp instrument for the two call sites <see cref="TestAltProfile"/>'s blunt id ceiling
    /// cannot probe: the biome map (which needs a scope tight enough to leave fewer biomes than the map's cap)
    /// and the evolved form (where every Gen 1 line starting under the ceiling also ends under it). Makes no more
    /// of a fidelity claim than that profile does.</summary>
    private sealed class SpeciesScope(Expression<Func<PokemonSpecies, bool>> keep) : IContentScope
    {
        public IQueryable<PokemonSpecies> Species(IQueryable<PokemonSpecies> all) =>
            all.Where(keep);

        public IQueryable<Attack> Moves(IQueryable<Attack> all) => all;

        public IQueryable<Item> Items(IQueryable<Item> all) => all;
    }

    /// <summary>A profile identical to Gen 1 except that it claims a generation the database holds no rows for.
    /// Deliberately <em>not</em> <see cref="TestAltProfile"/>: that one reuses <see cref="Generation.One"/> as its
    /// label (the enum has no second member), so it cannot probe the data-filter half at all. Its evolution rules
    /// also never evolve anything, which would make the evolution assertion above pass for the wrong reason.</summary>
    private static GenerationProfile UnseededGeneration() =>
        Gen1Profile.Instance with
        {
            Generation = (Generation)2,
        };

    private static void AssertBuiltByTheAltStatSeam(creaturegame.Creatures.Creature c) =>
        Assert.All(
            new[] { c.DvHP, c.DvAttack, c.DvDefense, c.DvSpecial, c.DvSpeed },
            dv => Assert.Equal(TestAltProfile.SentinelDv, dv)
        );
}

/// <summary>Test factory over the live SQLite DBs — mirrors the production composition (parameterless context
/// ctors resolve them). File-scoped, matching the sibling integration suites.</summary>
file sealed class LiveDbContextFactory<TContext>(Func<TContext> create)
    : Microsoft.EntityFrameworkCore.IDbContextFactory<TContext>
    where TContext : Microsoft.EntityFrameworkCore.DbContext
{
    public TContext CreateDbContext() => create();
}
