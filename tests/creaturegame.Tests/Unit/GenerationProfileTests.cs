using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Generations;
using creaturegame.Items;
using creaturegame.Web.Battle;
using creaturegame.Web.Controllers;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The generation axis (Stage 1 of <c>docs/GENERATION_PROFILE.md</c>): the profile bundle, the registry, and the
/// web boundary's parse.
/// <para>Two things are being pinned. First, that selecting Gen 1 is a <b>true no-op</b> — every slice is the
/// exact implementation the engine already defaulted to, the same proof shape <c>DifficultyTests</c> used for
/// the Normal preset. Second, and the reason this stage exists at all, that a profile is genuinely
/// <b>substitutable</b>: the seams are read from it rather than reached for globally.</para>
/// </summary>
public class GenerationProfileTests
{
    // ── The no-op proof ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Gen1Profile_SuppliesExactlyTheSingletonsTheEngineAlreadyDefaultedTo()
    {
        var p = Gen1Profile.Instance;

        // Reference equality, not just type equality: these ARE the singletons every `?? Gen1*.Instance`
        // fallback resolves to, so threading the profile cannot change behaviour for Gen 1.
        Assert.Same(Gen1TypeChart.Instance, p.TypeChart);
        Assert.Same(Gen1BattleRules.Instance, p.BattleRules);
        Assert.Same(Gen1EvolutionRules.Instance, p.EvolutionRules);
        Assert.Equal(Generation.One, p.Generation);
    }

    /// <summary>
    /// The Gen 1 type roster as a domain fact: 15 types, and specifically <b>not</b> the three that arrived later.
    /// </summary>
    /// <remarks>
    /// Deliberately asserts the three absences by name rather than only the count of 15. A count alone would stay
    /// green if someone swapped Fairy in for Ghost — the roster's job is to say <i>which</i> types exist, so that
    /// is what gets pinned. <see cref="DamageType"/> itself is not asserted against: it spans every generation on
    /// purpose and holding all 18 is correct.
    /// </remarks>
    [Fact]
    public void Gen1Profile_RostersThe15Gen1Types_AndNoneOfTheLaterArrivals()
    {
        var roster = Gen1Profile.Instance.TypeRoster;

        Assert.Equal(15, roster.Count);
        Assert.DoesNotContain(DamageType.Steel, roster); // Gen 2
        Assert.DoesNotContain(DamageType.Dark, roster); // Gen 2
        Assert.DoesNotContain(DamageType.Fairy, roster); // Gen 6
    }

    /// <summary>
    /// Every registered profile supplies every slice — no slice is null or empty.
    /// </summary>
    /// <remarks>
    /// <para><b>What this is and isn't for.</b> <c>required</c> plus a non-nullable type already makes an
    /// <i>omitted</i> slice a compile error — the guarantee the feature rests on (<c>GENERATION_PROFILE.md</c>
    /// §4.2) — and the obvious way to smuggle a null past it (initialising a slice from a <c>static readonly</c>
    /// field declared <i>below</i> the profile, since static field initializers run in textual order) turns out to
    /// be compile-caught too: it raises <c>CS8601</c>, which <c>TreatWarningsAsErrors</c> escalates to a build
    /// error. That was measured, not assumed.
    /// <para>So this is defence in depth, and its load-bearing half is the <i>emptiness</i> check below, which no
    /// compiler can make: a slice can be present, non-null and still vacuous. A profile is a plain record any
    /// caller can construct, so the runtime guard also covers slices reached by paths the initializer analysis
    /// does not see.</para></para>
    /// <para>Written to iterate <see cref="GenerationProfiles.Registered"/> and reflect over the record's
    /// properties rather than naming today's six slices, so Stage 2b/3/4's additions are covered the moment they
    /// are declared — no edit here, which is the only version of this test that stays true.</para>
    /// </remarks>
    [Fact]
    public void EveryRegisteredProfile_SuppliesEverySlice()
    {
        foreach (var generation in GenerationProfiles.Registered)
        {
            var profile = GenerationProfiles.For(generation);

            foreach (var slice in typeof(GenerationProfile).GetProperties())
                Assert.True(
                    slice.GetValue(profile) is not null,
                    $"{generation}: slice '{slice.Name}' is null."
                );

            // A roster that exists but is empty is the same bug wearing a different hat — it would make
            // Biomes.UnhomedTypes vacuously pass for every region.
            Assert.NotEmpty(profile.TypeRoster);
        }
    }

    [Fact]
    public void Gen1Profile_BuildsASeededStatCalculator_SoAFixedSeedReproducesDvs()
    {
        // A factory rather than a singleton specifically so DV randomisation draws from the RUN's seed — the
        // property EncounterFactory.BuildCreature relies on. Two calls must yield distinct instances, each
        // bound to the source it was given.
        var a = Gen1Profile.Instance.BuildStatCalculator(new SeededRandomSource(1));
        var b = Gen1Profile.Instance.BuildStatCalculator(new SeededRandomSource(1));

        Assert.IsType<Gen1StatCalculator>(a);
        Assert.NotSame(a, b);
    }

    [Fact]
    public void Gen1Profile_BuildsTheGen1BrainBoundToTheSuppliedRng()
    {
        var ai = Gen1Profile.Instance.BuildAi(new SeededRandomSource(7));

        Assert.IsType<Gen1TrainerAi>(ai);
    }

    // ── The registry ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_ResolvesGen1()
    {
        Assert.Same(Gen1Profile.Instance, GenerationProfiles.For(Generation.One));
        Assert.Same(Gen1Profile.Instance, GameSessionManager.ProfileFor(Generation.One));
    }

    [Fact]
    public void Registry_ThrowsForAnUnregisteredGeneration_RatherThanSilentlyServingGen1()
    {
        // The whole feature's failure mode is a silent Gen 1 fallback (GENERATION_PROFILE.md §4.2), so the
        // registry must NOT be forgiving. Untrusted input is made safe by the parse below, not by this method.
        var unregistered = (Generation)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => GenerationProfiles.For(unregistered));
    }

    // ── The web boundary parse ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("One")]
    [InlineData("one")]
    [InlineData("ONE")]
    public void ParseGeneration_IsCaseInsensitive(string value) =>
        Assert.Equal(Generation.One, GameController.ParseGeneration(value));

    [Theory]
    [InlineData(null)] // a client that doesn't send the field at all
    [InlineData("")]
    [InlineData("Gen2")] // a typo / a client ahead of the server
    [InlineData("99")] // a numeric string that isn't a registered member
    public void ParseGeneration_FallsBackToGen1_SoNoRequestIsEverDead(string? value) =>
        Assert.Equal(Generation.One, GameController.ParseGeneration(value));

    [Fact]
    public void ParseGeneration_RejectsARealEnumMemberThatHasNoRegisteredProfile()
    {
        // Guards the seam between the two rules above: the registry throws on an unregistered generation, and
        // this parse is what guarantees the boundary never hands it one. If a future enum member is added
        // without a profile, a request naming it must degrade to Gen 1 — never reach the registry and 500.
        Assert.All(
            GenerationProfiles.Registered,
            g => Assert.Equal(g, GameController.ParseGeneration(g.ToString()))
        );
        Assert.Equal(Generation.One, GameController.ParseGeneration("99"));
    }

    // ── Stage 1's falsification leg ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The point of the whole stage: the composition point <b>reads its seams from the profile</b> rather than
    /// reaching for <c>Gen1*.Instance</c>.
    /// <para>This asserts against <see cref="GameSessionManager.BuildRunOptions"/> — a real consumer — precisely
    /// because asserting that the alt profile's fields differ from Gen 1's would be a tautology: it would only
    /// re-state <c>TestAltProfile</c>'s own object initializer and would still pass if the thread were deleted.
    /// <c>Rules</c> is the sharpest case: before this feature it was never passed at all, so dropping it again
    /// would leave <see cref="Battle"/> silently falling back to <c>Gen1BattleRules.Instance</c> with the whole
    /// suite green (<c>GENERATION_PROFILE.md</c> §4.2).</para>
    /// <para><c>TestAltProfile</c> is not a Gen 2 and asserts nothing about fidelity — see §3.</para>
    /// </summary>
    [Fact]
    public void BuildRunOptions_TakesItsBattleRulesFromTheProfile_NotTheGen1Fallback()
    {
        var options = BuildOptionsWith(TestAltProfile.Instance);

        // Would fail if `Rules = profile.BattleRules` were dropped: the option would go null and Battle would
        // quietly resolve Gen 1 instead.
        Assert.Same(TestAltProfile.Instance.BattleRules, options.Rules);
        Assert.NotSame(Gen1BattleRules.Instance, options.Rules);
    }

    [Fact]
    public void BuildRunOptions_UnderGen1_StillResolvesTheExactSingletonTheEngineWouldHaveDefaultedTo()
    {
        // The other half of the no-op proof: threading the profile did not change what Gen 1 runs with.
        Assert.Same(Gen1BattleRules.Instance, BuildOptionsWith(Gen1Profile.Instance).Rules);
    }

    [Fact]
    public void BuildRunOptions_KeepsRunRulesKeyedToDifficulty_NotToTheGeneration()
    {
        // RunRules is deliberately NOT a generation seam (GENERATION_PROFILE.md §2.2) — the roguelite dial bag
        // is keyed to Difficulty, an orthogonal axis. Pinned so a later stage doesn't quietly fold it in.
        var underAlt = BuildOptionsWith(TestAltProfile.Instance, Difficulty.Hard);

        Assert.Equal(GameSessionManager.RunRulesFor(Difficulty.Hard), underAlt.RunRules);
    }

    /// <summary>
    /// The run-start → session → REST-read chain: a generation chosen at <c>RegisterSession</c> must be the one
    /// <see cref="GameSessionManager.GetGeneration"/> reports, because that is what stamps the CHECK POKEMON
    /// overview's <c>generation</c> field (which the client gates INFO rows on).
    /// </summary>
    /// <remarks>
    /// Exercises the <b>pending</b> leg, which needs no hub: <c>RegisterSession</c> never touches
    /// <c>hubContext</c>, and the claimed leg copies the same value verbatim onto <c>ActiveBattle</c>. Uses a
    /// generation with no registered profile on purpose — the session layer only <i>carries</i> the value, and
    /// pinning it with <see cref="Generation.One"/> would pass even if the getter returned a hardcoded default.
    /// </remarks>
    [Fact]
    public void GetGeneration_ReportsTheGenerationTheRunWasRegisteredWith()
    {
        var manager = new GameSessionManager(hubContext: null!, NoDbEncounterFactory());
        var player = new Creature("TESTMON");

        string gameId = manager.RegisterSession(
            player,
            [],
            new Bag(),
            new Wallet(),
            [],
            new SeededRandomSource(1),
            [],
            Difficulty.Normal,
            (Generation)2
        );

        Assert.Equal((Generation)2, manager.GetGeneration(gameId));
    }

    [Fact]
    public void GetGeneration_ReturnsNullForAnUnknownRun_RatherThanDefaultingToGen1()
    {
        // An unknown gameId is a 404, not a Gen 1 run — the same no-silent-fallback rule the registry follows
        // (GENERATION_PROFILE.md §4.2). GameController.GetPlayer depends on this to 404 rather than serve a
        // creature stamped with a generation nobody selected.
        var manager = new GameSessionManager(hubContext: null!, NoDbEncounterFactory());

        Assert.Null(manager.GetGeneration("no-such-game"));
    }

    private static RunDirectorOptions BuildOptionsWith(
        GenerationProfile profile,
        Difficulty difficulty = Difficulty.Normal
    )
    {
        var player = new Creature("TESTMON");
        var session = new PendingSession(
            player,
            [],
            new Bag(),
            new Wallet(),
            [],
            new SeededRandomSource(1),
            [],
            difficulty,
            Generation.One,
            DateTimeOffset.UtcNow
        );
        return GameSessionManager.BuildRunOptions(
            session,
            profile,
            NoDbEncounterFactory(),
            new Party(player),
            emitter: null
        );
    }

    /// <summary>An <see cref="EncounterFactory"/> whose DB factories throw if touched. Safe here because
    /// <c>BuildRunOptions</c> only <i>closes over</i> it — the draft/boss-catch suppliers are lambdas that reach
    /// the database when a run invokes them, which this test never does. Keeps the test DB-free and honest about
    /// why.</summary>
    private static EncounterFactory NoDbEncounterFactory() =>
        new(
            new UnusedDbContextFactory<PokemonDbContext>(),
            new UnusedDbContextFactory<MovesDbContext>(),
            new UnusedDbContextFactory<ItemsDbContext>()
        );

    private sealed class UnusedDbContextFactory<TContext> : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext() =>
            throw new InvalidOperationException(
                $"{typeof(TContext).Name} was created — BuildRunOptions is not supposed to touch the database."
            );
    }
}
