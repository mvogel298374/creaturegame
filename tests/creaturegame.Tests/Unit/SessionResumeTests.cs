using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Generations;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The lightweight `gameId`-persistence resume feature (ARCHITECTURE.md §2.7 — Session Resume). Everything
/// durable resume needs on the server side is already shipped (the reconnect grace); two real server-side gaps
/// this feature surfaced are pinned here. First: <see cref="GameSessionManager.AttachConnection"/> used to
/// silently no-op for an unknown/expired <c>gameId</c> — the SignalR connection still succeeded, so a client
/// resuming a dead session hung on "Connecting…" forever with no error. <c>AttachConnection</c> now reports
/// whether it actually attached, and <c>BattleHub</c> rejects the connection when it didn't. Second: a
/// reconnect that follows a full SPA remount (a refresh) restarts the client's React state at `initialState`,
/// with no live event queued to move it out of "Connecting…" — <see cref="SignalRBattleEventEmitter"/> now
/// caches the state-establishing events as they pass through and replays them via
/// <see cref="GameSessionManager.ReEstablishClient"/> on reconnect. The frontend fallback/Continue-button
/// plumbing is Vitest-covered client-side.
/// </summary>
public class SessionResumeTests
{
    private static Creature Player() => new("TESTMON");

    [Fact]
    public void AttachConnection_ReturnsFalse_ForAnUnknownGameId()
    {
        // Never reaches the hub or the DB on this path (both early-returns happen before either is touched),
        // so both dependencies are safe to leave unusable here.
        var manager = new GameSessionManager(hubContext: null!, NoDbEncounterFactory.Create());

        Assert.False(manager.AttachConnection("no-such-game", "conn-1"));
    }

    [Fact]
    public void AttachConnection_ReturnsTrue_OnAGenuineFirstAttach_AndOnTheReconnectThatFollows()
    {
        // A deterministic (gated) factory, not NoDbEncounterFactory: the spawned run task's own fault-and-remove
        // races an assertion that needs the battle to still be in the active set for the reconnect leg below.
        var hub = new RecordingHubContext();
        using var dbGate = new ManualResetEventSlim(initialState: false);
        var manager = new GameSessionManager(hub, BlockedEncounterFactory.Create(dbGate));
        string gameId = manager.RegisterSession(
            Player(),
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
            Assert.True(manager.AttachConnection(gameId, "conn-1")); // first attach: claims the pending session
            Assert.True(manager.AttachConnection(gameId, "conn-2")); // reconnect: rebinds the still-active battle
        }
        finally
        {
            dbGate.Set(); // let the parked run task die through its normal failure path
        }
    }

    // ── SignalRBattleEventEmitter.ReplayLastKnownState ────────────────────────────────────────────────────
    //
    // The other half of Session Resume: a reconnect that follows a full SPA remount (a refresh) needs the
    // state-establishing events resent, because the client's React state — and with it the only copy of "what's
    // on screen" — restarted at initialState. Each event otherwise fires exactly once, to whichever connection
    // was current at the time, so without this a reconnecting client would sit on "Connecting…" forever
    // (verified live in-browser during this feature's build). Three lifetimes, covered separately below:
    // run-scoped (RegionMapRevealed), biome-scoped (BiomeEntered / BiomeNodePlanRevealed), and battle-scoped
    // (BattleStarted / TurnStarted).

    private static TurnStarted Turn(int turnNumber) =>
        new(
            turnNumber,
            "Player",
            80,
            100,
            StatusCondition.None,
            0,
            100,
            "Enemy",
            40,
            50,
            StatusCondition.None,
            [],
            false
        );

    private static RegionMapRevealed Region() => new(1, 1, [], []);

    private static BiomeEntered Biome(string id) => new(id, id, [DamageType.Normal]);

    private static BiomeNodePlanRevealed NodePlan() => new(["WildBattle"]);

    [Fact]
    public void ReplayLastKnownState_IsANoOp_WhenNothingHasBeenEmittedYet()
    {
        var hub = new RecordingHubContext();
        var emitter = new SignalRBattleEventEmitter(hub, () => "conn-1");

        emitter.ReplayLastKnownState();

        Assert.Empty(hub.EventsFor("conn-1"));
    }

    [Fact]
    public void ReplayLastKnownState_ResendsEveryCachedEvent_InItsNaturalOrder_ToWhicheverConnectionIsNowCurrent()
    {
        var hub = new RecordingHubContext();
        string current = "conn-1";
        var emitter = new SignalRBattleEventEmitter(hub, () => current);

        emitter.Emit(Region());
        emitter.Emit(Biome("phantom-marsh"));
        emitter.Emit(NodePlan());
        emitter.Emit(new BattleStarted("Player", "Enemy", 1, 5));
        emitter.Emit(Turn(1));
        current = "conn-2"; // the reconnect: a new connection is now current

        emitter.ReplayLastKnownState();

        var replayed = hub.EventsFor("conn-2");
        Assert.Equal(
            [
                "RegionMapRevealed",
                "BiomeEntered",
                "BiomeNodePlanRevealed",
                "BattleStarted",
                "TurnStarted",
            ],
            replayed.Select(e => e.Type)
        );
        // Never re-sent to the old connection — matches how a live event follows a reconnect (ARCHITECTURE.md
        // §2.7), not just the replay.
        Assert.All(replayed, e => Assert.DoesNotContain(hub.EventsFor("conn-1"), old => old == e));
    }

    [Fact]
    public void ReplayLastKnownState_DoesNotReplayAStaleTurnStarted_FromTheBattleBeforeTheCurrentOne()
    {
        // A new BattleStarted means the new battle hasn't reached its first turn yet — replaying the PREVIOUS
        // battle's TurnStarted alongside it would show the wrong fight's HP/moves.
        var hub = new RecordingHubContext();
        var emitter = new SignalRBattleEventEmitter(hub, () => "conn-1");

        emitter.Emit(new BattleStarted("Player", "Enemy One", 1, 5));
        emitter.Emit(Turn(1));
        emitter.Emit(new BattleStarted("Player", "Enemy Two", 2, 6)); // next encounter starts; no TurnStarted yet

        emitter.ReplayLastKnownState();

        var events = hub.EventsFor("conn-1");
        Assert.Equal(3, events.Count(e => e.Type == "BattleStarted")); // the two originals + the replay
        Assert.Equal(1, events.Count(e => e.Type == "TurnStarted")); // only ever the one real TurnStarted
    }

    [Fact]
    public void ReplayLastKnownState_ClearsTheBattlePair_ButNotTheMapOrBiome_AfterBattleEnded()
    {
        // Between encounters (a route/shop/reward/etc. prompt — not yet covered by this replay, a named gap in
        // ARCHITECTURE.md §2.7) there is no live battle; replaying the just-finished one's stale state would be
        // actively wrong, not just incomplete. The run/biome-scoped state is different: the player is still on
        // the same map, in the same biome, regardless of how the last battle ended.
        var hub = new RecordingHubContext();
        var emitter = new SignalRBattleEventEmitter(hub, () => "conn-1");

        emitter.Emit(Region());
        emitter.Emit(Biome("phantom-marsh"));
        emitter.Emit(NodePlan());
        emitter.Emit(new BattleStarted("Player", "Enemy", 1, 5));
        emitter.Emit(Turn(1));
        emitter.Emit(new BattleEnded("Player"));
        int beforeReplay = hub.EventsFor("conn-1").Count;

        emitter.ReplayLastKnownState();

        var afterReplay = hub.EventsFor("conn-1");
        Assert.DoesNotContain(
            afterReplay.Skip(beforeReplay),
            e => e.Type is "BattleStarted" or "TurnStarted"
        );
        Assert.Contains(afterReplay.Skip(beforeReplay), e => e.Type == "RegionMapRevealed");
        Assert.Contains(afterReplay.Skip(beforeReplay), e => e.Type == "BiomeEntered");
        Assert.Contains(afterReplay.Skip(beforeReplay), e => e.Type == "BiomeNodePlanRevealed");
    }

    [Fact]
    public void ReplayLastKnownState_DoesNotReplayAStaleNodePlan_FromTheBiomeBeforeTheCurrentOne()
    {
        // A new BiomeEntered means that biome's node plan hasn't rolled yet — replaying the PREVIOUS biome's
        // plan alongside it would draw the wrong biome's encounter-map ladder.
        var hub = new RecordingHubContext();
        var emitter = new SignalRBattleEventEmitter(hub, () => "conn-1");

        emitter.Emit(Biome("whispering-woods"));
        emitter.Emit(NodePlan());
        emitter.Emit(Biome("phantom-marsh")); // next biome entered; its plan hasn't rolled yet

        emitter.ReplayLastKnownState();

        var events = hub.EventsFor("conn-1");
        Assert.Equal(3, events.Count(e => e.Type == "BiomeEntered")); // the two originals + the replay
        Assert.Equal(1, events.Count(e => e.Type == "BiomeNodePlanRevealed")); // only ever the one real plan
    }

    [Fact]
    public void ReplayLastKnownState_NeverClearsTheRegionMap_ItIsRunScoped()
    {
        // RegionMapRevealed fires at most once per run (docs on the event itself) — nothing in a normal run
        // ever re-emits or should clear it, battle-ending or biome-changing alike.
        var hub = new RecordingHubContext();
        var emitter = new SignalRBattleEventEmitter(hub, () => "conn-1");

        emitter.Emit(Region());
        emitter.Emit(Biome("whispering-woods"));
        emitter.Emit(new BattleStarted("Player", "Enemy", 1, 5));
        emitter.Emit(new BattleEnded("Player"));
        emitter.Emit(Biome("phantom-marsh"));
        int beforeReplay = hub.EventsFor("conn-1").Count;

        emitter.ReplayLastKnownState();

        Assert.Contains(
            hub.EventsFor("conn-1").Skip(beforeReplay),
            e => e.Type == "RegionMapRevealed"
        );
    }

    // ── GameSessionManager.ReEstablishClient ──────────────────────────────────────────────────────────────

    [Fact]
    public void ReEstablishClient_EchoesThePresentation_ThenReplaysTheCachedStateInOrder()
    {
        // Pins the reconnect branch's actual two-call sequence (extracted from AttachConnection so it's
        // independently testable — deleting or reordering either call here would stay green under every
        // scenario a normal run-through exercises, since inlined this wiring line has no dedicated coverage).
        var hub = new RecordingHubContext();
        var emitter = new SignalRBattleEventEmitter(hub, () => "conn-1");
        emitter.Emit(Region());
        emitter.Emit(Biome("phantom-marsh"));
        emitter.Emit(NodePlan());
        emitter.Emit(new BattleStarted("Player", "Enemy", 1, 5));
        emitter.Emit(Turn(1));
        int beforeReEstablish = hub.EventsFor("conn-1").Count;

        GameSessionManager.ReEstablishClient(emitter, Gen1Profile.Instance);

        var sent = hub.EventsFor("conn-1").Skip(beforeReEstablish).Select(e => e.Type);
        Assert.Equal(
            [
                "RunPresentationRevealed",
                "RegionMapRevealed",
                "BiomeEntered",
                "BiomeNodePlanRevealed",
                "BattleStarted",
                "TurnStarted",
            ],
            sent
        );
    }

    [Fact]
    public void ReEstablishClient_ToleratesANullEmitter()
    {
        // A pending (not-yet-claimed) session has no emitter yet; guards against a future call site reaching
        // this before AttachConnection's first-attach branch constructs one.
        GameSessionManager.ReEstablishClient(null, Gen1Profile.Instance);
    }
}
