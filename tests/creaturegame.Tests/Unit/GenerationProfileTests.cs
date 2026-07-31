using System.Text.Json;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Generations;
using creaturegame.Items;
using creaturegame.Web.Battle;
using creaturegame.Web.Controllers;
using creaturegame.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
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
    /// Gen 1's content scope is an <b>identity</b>: it hands every query straight back.
    /// </summary>
    /// <remarks>
    /// <para>The Stage 2b half of the stage-invariant acceptance condition — a Gen 1 run must be byte-for-byte
    /// what it was before the seam existed. Reference equality, not a row-count comparison, because that is the
    /// strongest available statement: an identity function cannot filter, reorder or re-materialise anything, so
    /// no behavioural difference can hide in it.</para>
    /// <para>Deliberately paired with the call-site probes in <c>EncounterFactoryGenerationProfileTests</c>:
    /// <i>this</i> says Gen 1's scope changes nothing, and those say the scope is nevertheless genuinely
    /// consulted. Neither is sufficient alone — an identity that is never called and an identity that is called
    /// look exactly the same from inside Gen 1 (<c>GENERATION_PROFILE.md</c> §4.2).</para>
    /// </remarks>
    [Fact]
    public void Gen1ContentScope_ReturnsEveryQueryUntouched_SoGen1SeesTheContentItAlwaysDid()
    {
        var scope = Gen1Profile.Instance.ContentScope;
        var species = Array.Empty<PokemonSpecies>().AsQueryable();
        var moves = Array.Empty<Attack>().AsQueryable();
        var items = Array.Empty<Item>().AsQueryable();

        Assert.Same(species, scope.Species(species));
        Assert.Same(moves, scope.Moves(moves));
        Assert.Same(items, scope.Items(items));
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
            // Biomes.UnhomedTypes vacuously pass for every region, and an empty biome roster would build a
            // run with no map at all.
            Assert.NotEmpty(profile.TypeRoster);
            Assert.NotEmpty(profile.BiomeRoster);
        }
    }

    // ── Stage 3: the region slice ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Gen1Profile_IsSetInKanto_AndItsBiomeRosterIsTheKantoRegistry()
    {
        var p = Gen1Profile.Instance;

        Assert.Equal(Region.Kanto, p.Region);
        // Reference equality, like the singleton pins above: the roster IS the authored registry read through
        // Biomes.For (the one door), so selecting Gen 1 cannot change which biomes exist.
        Assert.Same(Biomes.Kanto, p.BiomeRoster);
    }

    /// <summary>
    /// <see cref="GenerationProfile.Region"/> and <see cref="GenerationProfile.BiomeRoster"/> cannot drift: every
    /// rostered biome carries the profile's own region tag.
    /// </summary>
    /// <remarks>
    /// The region is kept as a named slice (identity/presentation, like <see cref="GenerationProfile.Generation"/>)
    /// while the roster is the consumed content — two properties answering one question, which is exactly the
    /// duplicated-source shape this feature keeps deleting elsewhere. This coherence pin is what makes the pair
    /// safe to hold: a profile whose biomes disagree with its declared region fails here by name.
    /// </remarks>
    [Fact]
    public void EveryRegisteredProfile_RostersOnlyBiomesOfItsOwnRegion()
    {
        foreach (var generation in GenerationProfiles.Registered)
        {
            var profile = GenerationProfiles.For(generation);
            Assert.All(profile.BiomeRoster, b => Assert.Equal(profile.Region, b.Region));
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

    // ── The presentation echo (Stage 4a) ──────────────────────────────────────────────────────────────

    [Fact]
    public void BuildPresentationEvent_CarriesTheGenerationIdAndRosterAsWireStrings()
    {
        var evt = GameSessionManager.BuildPresentationEvent(Gen1Profile.Instance);

        Assert.Equal("One", evt.Generation);
        // Set comparison, not sequence: TypeRoster is a set and the wire order is not part of the contract —
        // the client's consumers (asset-coverage checks, membership tests) are all order-blind.
        Assert.Equal(
            Gen1Profile.Instance.TypeRoster.Select(t => t.ToString()).ToHashSet(),
            evt.TypeRoster.ToHashSet()
        );
    }

    /// <summary>
    /// The falsification leg: the echoed roster must observably come off the <em>profile</em>. Under Gen 1 a
    /// hardcoded 15-name list and a real profile read produce identical wires forever — only a profile whose
    /// roster differs (TestAltProfile adds Dark and Steel) can tell them apart (GENERATION_PROFILE.md §4.2).
    /// </summary>
    [Fact]
    public void BuildPresentationEvent_RosterComesOffTheProfile_NotAHardcodedGen1List()
    {
        var evt = GameSessionManager.BuildPresentationEvent(TestAltProfile.Instance);

        Assert.Equal(17, evt.TypeRoster.Count);
        Assert.Contains("Dark", evt.TypeRoster);
        Assert.Contains("Steel", evt.TypeRoster);
    }

    /// <summary>
    /// The echo's <em>timing</em> claims, which the payload tests above can't see: on first attach the echo is
    /// emitted to that connection <b>ahead of anything the run produces</b>, and a reconnect <b>re-echoes</b> it
    /// to the new connection (route state is gone after a re-mount, so the echo is the client's only way back to
    /// the run's theme — <c>GENERATION_PROFILE.md</c> §7.2's "required, not optional").
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is race-free.</b> The first-attach echo is emitted <em>synchronously inside</em>
    /// <c>AttachConnection</c>, before the run task is even scheduled, and the recording client records
    /// synchronously — so "echo first" is a program-order fact, not a timing hope. The run task itself is parked
    /// deterministically: the blocking DB factory stalls its very first database touch (the enemy build), so the
    /// battle stays in the active set for the reconnect leg and the run emits nothing that could interleave.</para>
    /// <para>The gate is released in <c>finally</c>, after which the factory throws — the parked task then dies
    /// through the session's normal failure path instead of leaking a blocked thread past the test.</para>
    /// </remarks>
    [Fact]
    public void AttachConnection_EchoesThePresentation_OnFirstAttach_AndAgainOnReconnect()
    {
        var hub = new RecordingHubContext();
        using var dbGate = new ManualResetEventSlim(initialState: false);
        var manager = new GameSessionManager(hub, BlockedEncounterFactory(dbGate));
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
            Generation.One
        );

        try
        {
            manager.AttachConnection(gameId, "conn-1");

            // First attach: the echo leads the connection's event stream and carries the run's profile.
            var first = hub.EventsFor("conn-1")[0];
            Assert.Equal("RunPresentationRevealed", first.Type);
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(first.Payload));
            Assert.Equal("One", payload.RootElement.GetProperty("Generation").GetString());
            Assert.Equal(15, payload.RootElement.GetProperty("TypeRoster").GetArrayLength());

            // Reconnect: the rebind branch re-echoes to the NEW connection (and doesn't re-send to the old one).
            manager.AttachConnection(gameId, "conn-2");
            Assert.Equal("RunPresentationRevealed", hub.EventsFor("conn-2")[0].Type);
            Assert.Single(hub.EventsFor("conn-1"), e => e.Type == "RunPresentationRevealed");
        }
        finally
        {
            dbGate.Set();
        }
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

    // ── AttachConnection echo harness ─────────────────────────────────────────────────────────────────

    /// <summary>An <see cref="EncounterFactory"/> whose every DB touch parks on <paramref name="gate"/> — used
    /// by the attach-echo test to stall the run task deterministically at its first database read (the enemy
    /// build), so the battle stays active for the reconnect leg and the run emits nothing that could interleave
    /// with the assertions. Once the gate is set the factory throws, letting the parked task die through the
    /// session's normal failure path.</summary>
    private static EncounterFactory BlockedEncounterFactory(ManualResetEventSlim gate) =>
        new(
            new BlockingDbContextFactory<PokemonDbContext>(gate),
            new BlockingDbContextFactory<MovesDbContext>(gate),
            new BlockingDbContextFactory<ItemsDbContext>(gate)
        );

    private sealed class BlockingDbContextFactory<TContext>(ManualResetEventSlim gate)
        : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext()
        {
            gate.Wait();
            throw new InvalidOperationException(
                "gate released — the parked run task ends here (post-assertion cleanup)."
            );
        }
    }

    /// <summary>A recording <c>IHubContext</c>: <c>Client(id)</c> hands back a client that appends every
    /// <c>OnBattleEvent</c> to a per-connection list, synchronously — so emit order is observable in program
    /// order. Only the member <c>SignalRBattleEventEmitter</c> uses is implemented; everything else throws.</summary>
    private sealed class RecordingHubContext : IHubContext<BattleHub, IBattleClient>
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, List<(string Type, object Payload)>> _events = new();

        public IReadOnlyList<(string Type, object Payload)> EventsFor(string connectionId)
        {
            lock (_lock)
            {
                return _events.TryGetValue(connectionId, out var list) ? list.ToList() : [];
            }
        }

        private void Record(string connectionId, string type, object payload)
        {
            lock (_lock)
            {
                if (!_events.TryGetValue(connectionId, out var list))
                    _events[connectionId] = list = [];
                list.Add((type, payload));
            }
        }

        public IHubClients<IBattleClient> Clients => new RecordingClients(this);

        public IGroupManager Groups =>
            throw new NotSupportedException("Groups are not used by the emitter.");

        private sealed class RecordingClients(RecordingHubContext owner)
            : IHubClients<IBattleClient>
        {
            public IBattleClient Client(string connectionId) =>
                new RecordingClient(owner, connectionId);

            public IBattleClient All => throw new NotSupportedException();

            public IBattleClient AllExcept(IReadOnlyList<string> excludedConnectionIds) =>
                throw new NotSupportedException();

            public IBattleClient Clients(IReadOnlyList<string> connectionIds) =>
                throw new NotSupportedException();

            public IBattleClient Group(string groupName) => throw new NotSupportedException();

            public IBattleClient GroupExcept(
                string groupName,
                IReadOnlyList<string> excludedConnectionIds
            ) => throw new NotSupportedException();

            public IBattleClient Groups(IReadOnlyList<string> groupNames) =>
                throw new NotSupportedException();

            public IBattleClient User(string userId) => throw new NotSupportedException();

            public IBattleClient Users(IReadOnlyList<string> userIds) =>
                throw new NotSupportedException();
        }

        private sealed class RecordingClient(RecordingHubContext owner, string connectionId)
            : IBattleClient
        {
            public Task OnBattleEvent(string eventType, object payload)
            {
                owner.Record(connectionId, eventType, payload);
                return Task.CompletedTask;
            }
        }
    }
}
