using creaturegame.Combat;
using creaturegame.DB;
using creaturegame.Generations;
using creaturegame.Tests.Unit;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// Stage 1b's falsification leg (<c>docs/GENERATION_PROFILE.md</c> §3): proves <see cref="EncounterFactory"/>
/// reads <b>both</b> of its generation-variable inputs off the run's profile rather than from the hardcoded
/// Gen 1 sources it used to.
/// <para>Two separate leaks are covered, because they failed in two different ways:</para>
/// <list type="bullet">
/// <item><b>The stat seam</b> — <c>BuildCreature</c> constructed its own <c>new Gen1StatCalculator(rng)</c>, so
/// a profile could supply any <c>BuildStatCalculator</c> it liked and be silently ignored.</item>
/// <item><b>The generation as a data filter</b> — a <c>private const int ActiveGeneration = 1</c> drove six
/// learnset/evolution queries, so a non-Gen-1 run would still have been served Gen 1 rows.</item>
/// </list>
/// <para>Both are the shape <c>§4.2</c> warns about: before this stage, deleting the thread would have left the
/// whole suite green, because Gen 1 was the answer either way. These tests are only meaningful because a
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

        var boss = new creaturegame.Creatures.Creature("GYARADOS") { Level = 42, SpeciesId = 130 };
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
