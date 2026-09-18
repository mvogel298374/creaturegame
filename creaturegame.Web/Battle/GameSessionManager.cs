using System.Collections.Concurrent;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Generations;
using creaturegame.Items;
using creaturegame.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Web.Battle;

public sealed class GameSessionManager(
    IHubContext<BattleHub, IBattleClient> hubContext,
    EncounterFactory encounters
)
{
    private readonly ConcurrentDictionary<string, PendingSession> _pending = new(); // gameId → registered, not yet started
    private readonly ConcurrentDictionary<string, ActiveBattle> _active = new(); // gameId → running battle
    private readonly ConcurrentDictionary<string, string> _connToGame = new(); // connectionId → gameId (routing)

    // Pending sessions never claimed (client never connected) are evicted after this TTL.
    private static readonly TimeSpan PendingSessionTtl = TimeSpan.FromMinutes(2);

    // Reconnect grace after a disconnect, before the battle is abandoned — covers the JS client's automatic-
    // reconnect policy (gives up ~30s). Mechanism → ARCHITECTURE.md §2.7.
    private static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(40);

    // The Easy/Normal/Hard RunRules presets — a roguelite dial bag kept out of the Gen-1 IBattleRules seam.
    // Normal reproduces the pre-difficulty-slider numbers exactly (a true no-op). Full rationale + the curve's
    // anchors → GENERATION_SEAMS.md (RunRules / XP curve / Innate Party XP Share).
    private static readonly IReadOnlyDictionary<Difficulty, RunRules> RunTuningByDifficulty =
        new Dictionary<Difficulty, RunRules>
        {
            [Difficulty.Easy] = new RunRules
            {
                XpMultiplierEarly = 2.0,
                XpMultiplierLate = 6.0,
                BenchXpShare = 0.75,
            },
            [Difficulty.Normal] = new RunRules
            {
                XpMultiplierEarly = 1.5,
                XpMultiplierLate = 4.5,
                BenchXpShare = 0.5,
            },
            [Difficulty.Hard] = new RunRules
            {
                XpMultiplierEarly = 1.0,
                XpMultiplierLate = 3.0,
                BenchXpShare = 0.25,
            },
        };

    /// <summary>The <see cref="RunRules"/> preset for a difficulty tier. <c>internal</c> (not private) so the
    /// preset values themselves — and not just the surrounding wiring — are directly testable.</summary>
    internal static RunRules RunRulesFor(Difficulty difficulty) =>
        RunTuningByDifficulty[difficulty];

    /// <summary>The <see cref="GenerationProfile"/> for a generation. <c>internal</c> for the same reason as
    /// <see cref="RunRulesFor"/> — so the lookup itself is exercised by tests rather than a duplicate of it.
    /// A thin pass-through to the registry, kept here so the session layer has one named door to it.</summary>
    internal static GenerationProfile ProfileFor(Generation generation) =>
        GenerationProfiles.For(generation);

    /// <summary>Builds the presentation echo (<see cref="RunPresentationRevealed"/>) for a run's profile: the
    /// generation id plus the profile's type roster, both as wire strings. Pure + <c>internal</c> like
    /// <see cref="BuildRunOptions"/>, and for the same reason — the roster must observably come <em>off the
    /// profile</em> (a hardcoded 15 would stay green under Gen 1 forever), so the test pins this against
    /// a second profile.</summary>
    internal static RunPresentationRevealed BuildPresentationEvent(GenerationProfile profile) =>
        new(profile.Generation.ToString(), profile.TypeRoster.Select(t => t.ToString()).ToList());

    /// <summary>Everything a reconnecting client needs re-sent to it, in order: the presentation echo (theme —
    /// GENERATION_PROFILE.md §7.2) first, then whatever state-establishing events the emitter has cached
    /// (SignalRBattleEventEmitter.ReplayLastKnownState — map/biome/battle/turn, each only if that slice is
    /// currently live). Extracted from <see cref="AttachConnection"/>'s reconnect branch for the same reason as
    /// <see cref="BuildRunOptions"/>: pulled out, the two-call sequence is <b>observable to a test</b> — inlined,
    /// deleting either call (or reordering them) would stay green under every scenario a normal run-through
    /// exercises, since the theme rarely matters for correctness and the replay is silent when its cache is
    /// empty. <c>internal static</c> and dependency-free by design, like its siblings above.</summary>
    internal static void ReEstablishClient(
        SignalRBattleEventEmitter? emitter,
        GenerationProfile profile
    )
    {
        // Re-echo the presentation identity on every reconnect — GAME_LOOP.md §5 "session-layer events".
        emitter?.Emit(BuildPresentationEvent(profile));
        // A reconnect that follows a transient network drop needs nothing more — the client's React state never
        // unmounted, so it already has the current run on screen. A reconnect that follows a full SPA remount
        // (a refresh) does need this: its state restarted at 'connecting', with no way out short of the
        // state-establishing events replaying — see ARCHITECTURE.md §2.7.
        emitter?.ReplayLastKnownState();
    }

    /// <summary>Assembles the run's <see cref="RunDirectorOptions"/> — the run-scoped policy bag handed to the
    /// director.</summary>
    /// <remarks>
    /// Extracted from <see cref="AttachConnection"/> so the seams a run is configured with are <b>observable to
    /// a test</b>: with everything inline, dropping <c>Rules = profile.BattleRules</c> would leave the suite
    /// green because of the engine's silent fallback (<c>docs/GENERATION_PROFILE.md</c> §4.2) — pinning this
    /// method against a second profile turns "the profile is threaded" from a claim into a test
    /// (<c>GenerationProfileTests</c>). <c>internal static</c> and dependency-free by design: everything it
    /// needs is a parameter, so a test can call it without standing up a hub, a connection, or a battle.
    /// </remarks>
    internal static RunDirectorOptions BuildRunOptions(
        PendingSession session,
        GenerationProfile profile,
        EncounterFactory encounters,
        Party party,
        IBattleEventEmitter? emitter
    ) =>
        new()
        {
            Emitter = emitter,
            Rng = session.Rng,
            Rules = profile.BattleRules,
            CheckEvolution = p =>
                encounters.ResolvePlayerEvolutionAsync(p, session.AllMoves, profile),
            PlayerBag = session.Bag,
            // Non-empty ⇒ biome traversal instead of the legacy endless chain — ENCOUNTER_DESIGN.md §7 Phase 3b-2.
            PlayableBiomes = session.PlayableBiomes,
            Wallet = session.Wallet,
            RewardSupplier = EncounterFactory.BuildRewardSupplier(session.AllItems),
            ShopSupplier = EncounterFactory.BuildShopSupplier(session.AllItems),
            // Gates a Shop node on affordability, so a broke player never gets a dead all-unaffordable shop.
            MinShopBudget = ShopCalculator.MinItemPrice,
            // Keyed to Difficulty, not generation — RunRules is deliberately not a seam (GENERATION_PROFILE.md §2.2).
            RunRules = RunRulesFor(session.Difficulty),
            Party = party,
            DraftSupplier = encounters.BuildDraftSupplier(session.AllMoves, profile),
            BossCatchSupplier = encounters.BuildBossCatchSupplier(session.AllMoves, profile),
        };

    public string RegisterSession(
        Creature player,
        IReadOnlyList<Attack> allMoves,
        Bag bag,
        Wallet wallet,
        IReadOnlyList<Item> allItems,
        IRandomSource rng,
        IReadOnlyList<BiomeDefinition> playableBiomes,
        // Both deliberately UN-defaulted. A defaulted run parameter here is the silent-fallback shape this
        // feature exists to close (docs/GENERATION_PROFILE.md §4.2): a future entry point — resume-run, a dev
        // endpoint, a test harness — that omitted the argument would compile, run, and quietly play Gen 1 on
        // Normal. Costs nothing to require: there is exactly one caller (GameController.Start).
        Difficulty difficulty,
        Generation generation
    )
    {
        var gameId = Guid.NewGuid().ToString("N");
        _pending[gameId] = new PendingSession(
            player,
            allMoves,
            bag,
            wallet,
            allItems,
            rng,
            playableBiomes,
            difficulty,
            generation,
            DateTimeOffset.UtcNow
        );
        EvictExpiredPendingSessions();
        return gameId;
    }

    private void EvictExpiredPendingSessions()
    {
        var cutoff = DateTimeOffset.UtcNow - PendingSessionTtl;
        foreach (var (key, session) in _pending)
            if (session.RegisteredAt < cutoff)
                _pending.TryRemove(key, out _);
    }

    /// <summary>
    /// Called on every hub connection. The first connection for a gameId starts the battle;
    /// a later connection for an already-running gameId is a reconnect — it rebinds the
    /// battle to the new connection (events and input follow) and cancels any pending abandon.
    /// Returns whether the connection actually attached to something — <c>false</c> for a
    /// <paramref name="gameId"/> that is neither active nor pending (unknown, or past the
    /// pending-session TTL / reconnect grace). <see cref="Hubs.BattleHub"/> rejects the connection
    /// on <c>false</c> rather than leaving the caller attached to nothing — see ARCHITECTURE.md §2.7
    /// for why a silent no-op here was a real client-side hang.
    /// </summary>
    public bool AttachConnection(string gameId, string connectionId)
    {
        // Reconnect: an existing battle just needs to be repointed at the new connection.
        if (_active.TryGetValue(gameId, out var existing))
        {
            existing.CancelAbandon();
            var previous = existing.CurrentConnectionId;
            if (!string.IsNullOrEmpty(previous))
                _connToGame.TryRemove(previous!, out _);
            existing.CurrentConnectionId = connectionId;
            _connToGame[connectionId] = gameId;
            ReEstablishClient(existing.Emitter, ProfileFor(existing.Generation));
            return true;
        }

        // First connection: claim the pending session and start the battle loop.
        if (!_pending.TryRemove(gameId, out var session))
            return false; // unknown or already-consumed gameId

        // Resolved once and threaded into every seam consumer below; fixed for the whole run.
        var profile = ProfileFor(session.Generation);

        var battle = new ActiveBattle
        {
            CurrentConnectionId = connectionId,
            Player = session.Player,
            // The session owns this single Party instance so the party-hydrate endpoint and the RunDirector's
            // RunState read the same roster.
            Party = new Party(session.Player),
            Bag = session.Bag,
            Wallet = session.Wallet,
            ItemsById = session.AllItems.ToDictionary(i => i.Id),
            Generation = session.Generation,
        };
        _active[gameId] = battle;
        _connToGame[connectionId] = gameId;

        // Held on the battle so the reconnect branch above re-echoes through the same instance (ARCHITECTURE.md
        // §2.7); emitted here first so the client can theme itself ahead of the first battle event.
        var emitter = new SignalRBattleEventEmitter(hubContext, () => battle.CurrentConnectionId);
        battle.Emitter = emitter;
        emitter.Emit(BuildPresentationEvent(profile));
        // Gen1TrainerAi (an intelligent-but-fallible Gen 1 move selector, TODO_ARCHIVE.md) drives the enemy; a
        // single instance is safe to share/reuse across encounters — the run is single-threaded. The seeded
        // session.Rng threads through every nondeterministic step (ARCHITECTURE.md §2.10).
        var runner = new RunDirector(
            session.Player,
            (p, depth, biome, tier) =>
                encounters.CreateEnemyAsync(
                    p,
                    session.AllMoves,
                    profile,
                    session.Rng,
                    biome: biome,
                    depth: depth,
                    // Web-layer half of the node-tier intent/mapping split — ENCOUNTER_DESIGN.md §3.1.
                    archetype: EnemyArchetypes.For(tier, session.Rng)
                ),
            // THE generation composition point (GENERATION_SEAMS.md §7, GENERATION_PROFILE.md §4) — every seam
            // below is read explicitly off the run's profile, never left to an engine default.
            profile.TypeChart,
            battle.Input,
            new AiBattleInput(profile.BuildAi(session.Rng)),
            movePool: session.AllMoves,
            BuildRunOptions(session, profile, encounters, battle.Party, emitter)
        );

        _ = Task.Run(async () =>
        {
            try
            {
                await runner.RunAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when the reconnect grace expires — see DetachConnection. The run is abandoned
                // without a RunEnded (that's reserved for the player actually fainting).
                Console.WriteLine(
                    $"[GameSessionManager] Run {gameId} abandoned (client did not reconnect)."
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSessionManager] Run {gameId} failed: {ex}");
            }
            finally
            {
                battle.CancelAbandon();
                _active.TryRemove(gameId, out _);
                if (!string.IsNullOrEmpty(battle.CurrentConnectionId))
                    _connToGame.TryRemove(battle.CurrentConnectionId!, out _);
            }
        });

        return true;
    }

    /// <summary>
    /// The live player <see cref="Creature"/> for a game, for the on-demand overview snapshot (CHECK POKEMON).
    /// Checks the running battle first, then a not-yet-started pending session; null if the game is unknown.
    /// A display-only read of live state — no lock. The battle thread mutates concurrently, but the two
    /// fields this read enumerates are both safe against that: MoveSet is copy-on-write (mutations swing the
    /// reference, see Creature.MoveSet) and Bag is a ConcurrentDictionary. Worst case the snapshot is one tick
    /// stale; it never throws "Collection was modified".
    /// </summary>
    public Creature? GetPlayerCreature(string gameId)
    {
        if (_active.TryGetValue(gameId, out var battle) && battle.Player is not null)
            return ActiveCreature(battle.Party, battle.Player);
        if (_pending.TryGetValue(gameId, out var pending))
            return pending.Player;
        return null;
    }

    /// <summary>The creature at an arbitrary party slot — the CHECK POKEMON party-member picker (docs/TODO.md).
    /// Unlike <see cref="GetPlayerCreature(string)"/> this doesn't resolve to the active lead; it reads whichever
    /// slot the client asked for, fainted/benched included, so the picker can show any party member's sheet.
    /// Null on an unknown game or an out-of-range slot.</summary>
    public Creature? GetPlayerCreature(string gameId, int slot)
    {
        if (_active.TryGetValue(gameId, out var battle) && battle.Player is not null)
            return PartyMemberAt(battle.Party, battle.Player, slot);
        if (_pending.TryGetValue(gameId, out var pending))
            return PartyMemberAt(null, pending.Player, slot);
        return null;
    }

    /// <summary>The live <see cref="Party"/> instance behind an active battle — a test-only seam so a scenario
    /// can be grown to multiple members (<c>party.Add(...)</c>) without running the full acquisition flow.
    /// <c>internal</c> for the same reason as <see cref="RunRulesFor"/>: directly testable rather than only
    /// exercised through the full endpoint. Null for an unknown or not-yet-active game.</summary>
    internal Party? ActivePartyFor(string gameId) =>
        _active.TryGetValue(gameId, out var battle) ? battle.Party : null;

    /// <summary>The generation a run is being played under, or null if the gameId is unknown. Resolved in the
    /// same active-then-pending order as <see cref="GetPlayerCreature"/>, so a caller that needs both gets a
    /// consistent pair. Returns null rather than defaulting to <see cref="Generation.One"/> on purpose — an
    /// unknown run is a 404, not a Gen 1 run (<c>docs/GENERATION_PROFILE.md</c> §4.2).</summary>
    public Generation? GetGeneration(string gameId)
    {
        if (_active.TryGetValue(gameId, out var battle))
            return battle.Generation;
        if (_pending.TryGetValue(gameId, out var pending))
            return pending.Generation;
        return null;
    }

    /// <summary>The creature the panel should describe: the party's <em>live</em> lead when a party is wired,
    /// else the session's starter. The lead moves — the between-biome lead swap (Phase 4 Stage 1d) reassigns it,
    /// and since the forced faint-switch (Stage 3) so does a mid-battle send-in — so it must be resolved per read,
    /// never captured at session claim. Pure + internal so the rule is unit-testable on its own (as
    /// <c>ProjectBagView</c> is).</summary>
    internal static Creature? ActiveCreature(Party? party, Creature? starter) =>
        party?.Lead ?? starter;

    /// <summary>The rule behind <see cref="GetPlayerCreature(string, int)"/>: with no party wired, only slot 0
    /// (the starter) exists; with a party, any in-range index is valid regardless of lead/fainted state. Pure +
    /// internal so it's unit-testable on its own, same precedent as <see cref="ActiveCreature"/>.</summary>
    internal static Creature? PartyMemberAt(Party? party, Creature? starter, int slot) =>
        party is not null
            ? (slot >= 0 && slot < party.Members.Count ? party.Members[slot] : null)
            : (slot == 0 ? starter : null);

    public void SetMoveChoice(string connectionId, int moveIndex)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetChoice(moveIndex);
    }

    /// <summary>Routes a bag-item use to the battle's input: resolves the item from the run's catalog and
    /// completes the turn handshake. The engine's <c>ItemAction</c> does the has-in-bag + would-have-effect
    /// checks (a no-op use yields <c>ItemUseFailed</c> and the turn proceeds), so this only validates that the
    /// id is a real catalog item. An unknown id is ignored (a malformed client request).</summary>
    public void SetItemChoice(
        string connectionId,
        int itemId,
        int? targetMoveSlot,
        int? targetPartySlot
    )
    {
        if (
            !_connToGame.TryGetValue(connectionId, out var gameId)
            || !_active.TryGetValue(gameId, out var battle)
        )
            return;

        if (battle.ItemsById.TryGetValue(itemId, out var item))
        {
            battle.Input.SetItemChoice(item, targetMoveSlot, targetPartySlot);
        }
        else
        {
            // Unknown item id (malformed/stale client request): don't leave the turn handshake parked.
            // Advance the turn with a move fallback (ResolveMove(-1) → first selectable), mirroring the
            // out-of-range move-slot handling. ItemAction stays the authority for a *known* item that's out
            // of stock or would have no effect (it soft-fails with ItemUseFailed and the turn proceeds).
            battle.Input.SetChoice(-1);
        }
    }

    /// <summary>The run's current bag contents (held quantity joined with item data) for the bag UI, or null
    /// if the game is unknown / not yet started. Reads live session state — display-only.</summary>
    public IReadOnlyList<BagItemView>? GetBagContents(string gameId)
    {
        if (!_active.TryGetValue(gameId, out var battle) || battle.Bag is null)
            return null;
        return ProjectBagView(battle.Bag, battle.ItemsById, battle.Party);
    }

    /// <summary>Projects the held bag (id → qty) plus the item catalog into the client's <see cref="BagItemView"/>
    /// list, ordered by id. Pure (no session state) so the wire projection — notably the
    /// <see cref="BagItemView.UsableInBattle"/> flag — is unit-testable without standing up a live battle.
    /// <para><paramref name="party"/> is needed only to gate <b>Revive</b>: every other category's usability is a
    /// fixed property of the category regardless of party state, but a Revive is usable only when a
    /// <em>fainted</em> party member exists to target — so the menu hides it (rather than offering a
    /// guaranteed no-op) when the roster is all up.</para></summary>
    internal static IReadOnlyList<BagItemView> ProjectBagView(
        Bag bag,
        IReadOnlyDictionary<int, Item> itemsById,
        Party? party = null
    ) =>
        bag
            .Entries.Where(e => e.Value > 0 && itemsById.ContainsKey(e.Key))
            .Select(e =>
            {
                var item = itemsById[e.Key];
                return new BagItemView(
                    item.Id,
                    item.Name ?? "",
                    item.Category.ToString(),
                    e.Value,
                    item.Description ?? "",
                    item.RestoresPpAllMoves,
                    UsableInBattle(item, party)
                );
            })
            .OrderBy(v => v.Id)
            .ToList();

    /// <summary>Whether using <paramref name="item"/> in battle now would do anything — the client filters the bag
    /// menu on this instead of re-encoding the category→effect mapping. The base rule is the single source of truth
    /// for "does anything in battle": the engine's <see cref="ItemEffects"/> registry (Ball/Other have no effect ⇒
    /// hidden). <b>Revive is the one state-dependent category</b>: it has an effect, but only when a fainted party
    /// member exists to target — so it stays hidden while the whole roster is up (mirrors <c>ReviveItemEffect.CanApply</c>,
    /// which would otherwise refuse the use as a no-op).</summary>
    internal static bool UsableInBattle(Item item, Party? party)
    {
        if (ItemEffects.For(item.Category) is null)
            return false;
        if (item.Category == ItemCategory.Revive)
            return party is { } p && p.Members.Any(m => !m.IsAlive());
        return true;
    }

    /// <summary>Routes a level-up replace-move answer (slot 0–3, or null to decline) to the battle's input.</summary>
    public void SetForgetChoice(string connectionId, int? slotIndex)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetForgetChoice(slotIndex);
    }

    /// <summary>Routes a Poké Center recovery answer (true = heal, false = skip) to the battle's input.</summary>
    public void SetRecoveryChoice(string connectionId, bool accept)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetRecoveryChoice(accept);
    }

    /// <summary>Routes an evolution answer (true = evolve, false = cancel) to the battle's input.</summary>
    public void SetEvolutionChoice(string connectionId, bool allow)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetEvolutionChoice(allow);
    }

    /// <summary>Routes a reward-choice modal pick (the chosen option index) to the battle's input.</summary>
    public void SetRewardChoice(string connectionId, int index)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetRewardChoice(index);
    }

    /// <summary>Routes a shop buy/leave choice to the battle's input (the shop node loops on these).</summary>
    public void SetShopAction(string connectionId, ShopAction action)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetShopAction(action);
    }

    /// <summary>Routes an acquisition answer (decline / add / add-by-replacing a slot) to the battle's input.
    /// A decline or unhonourable accept is a no-op in the run loop, so this only forwards.</summary>
    public void SetAcquisitionDecision(string connectionId, AcquisitionDecision decision)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetAcquisitionDecision(decision);
    }

    /// <summary>Routes a between-biome lead pick (the chosen party-member index) to the battle's input. An
    /// out-of-range / unchanged index is a no-op in the run loop (keeps the current lead), so this only forwards.</summary>
    public void SetLeadChoice(string connectionId, int index)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetLeadChoice(index);
    }

    /// <summary>Routes a forced faint-switch pick (the chosen party-member index) to the battle's input. A stale /
    /// out-of-range / fainted index is corrected to the first live member in the engine, so this only forwards.</summary>
    public void SetSwitchInChoice(string connectionId, int index)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetSwitchInChoice(index);
    }

    /// <summary>Routes a voluntary in-battle SWITCH (the chosen party-member index) to the battle's input, as a
    /// whole-turn choice. The engine's <c>Battle.CanSwitchTo</c> validates the pick (in range / alive / not the
    /// active member / not trapped) and falls back to FIGHT on an illegal one, so this only forwards.</summary>
    public void SetSwitchChoice(string connectionId, int index)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetSwitchChoice(index);
    }

    /// <summary>The run's current party roster (the same wire shape as the pushed <c>PartyUpdated</c> event), for
    /// the roster panel to hydrate on load / after a reconnect — parity with <see cref="GetBagContents"/> /
    /// <see cref="GetWallet"/>. A running battle's live party first, else the not-yet-started session's lone
    /// starter (a party of one); null if the game is unknown. Projected through the emitter's member projector so
    /// the pulled snapshot matches the pushed events exactly.</summary>
    public IReadOnlyList<object>? GetParty(string gameId)
    {
        Party? party = null;
        if (_active.TryGetValue(gameId, out var battle) && battle.Party is not null)
            party = battle.Party;
        else if (_pending.TryGetValue(gameId, out var pending))
            party = new Party(pending.Player);
        if (party is null)
            return null;
        return PartyProjection
            .Snapshot(party)
            .Select(SignalRBattleEventEmitter.ProjectPartyMember)
            .ToList();
    }

    /// <summary>The run's current gold balance for the HUD, or null if the game is unknown / not yet started.
    /// Reads live session state — display-only, parity with <see cref="GetBagContents"/>.</summary>
    public int? GetWallet(string gameId)
    {
        if (!_active.TryGetValue(gameId, out var battle) || battle.Wallet is null)
            return null;
        return battle.Wallet.Balance;
    }

    /// <summary>Routes a map-screen route choice (the chosen biome id) to the battle's input. An unknown id is
    /// tolerated by the run loop (falls back to the first offered biome), so this only forwards.</summary>
    public void SetBiomeChoice(string connectionId, string biomeId)
    {
        if (
            _connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle)
        )
            battle.Input.SetBiomeChoice(biomeId);
    }

    /// <summary>
    /// Called when a connection drops. If it is the battle's current connection, a grace
    /// timer is started; the battle is abandoned (its input cancelled so the loop ends and
    /// is collected) only if no reconnect arrives in time. A stale drop — an old connection
    /// dropping after we have already rebound to a newer one — is ignored.
    /// </summary>
    public void DetachConnection(string connectionId)
    {
        if (!_connToGame.TryGetValue(connectionId, out var gameId))
            return;
        if (!_active.TryGetValue(gameId, out var battle))
            return;
        if (battle.CurrentConnectionId != connectionId)
            return; // stale old connection

        battle.ScheduleAbandon(ReconnectGrace);
    }
}

sealed record PendingSession(
    Creature Player,
    IReadOnlyList<Attack> AllMoves,
    Bag Bag,
    Wallet Wallet,
    IReadOnlyList<Item> AllItems,
    IRandomSource Rng,
    IReadOnlyList<BiomeDefinition> PlayableBiomes,
    Difficulty Difficulty,
    Generation Generation,
    DateTimeOffset RegisteredAt
);

/// <summary>
/// Roguelite difficulty tiers offered to the player at run-start (StarterSelection), each mapped to a preset
/// <see cref="RunRules"/> (see <c>GameSessionManager.RunTuningByDifficulty</c>). A web-layer/session concept
/// only — <see cref="RunRules"/> itself stays a generic tuning bag; this is just the named-preset lookup.
/// </summary>
public enum Difficulty
{
    Easy,
    Normal,
    Hard,
}

/// <summary>A bag entry for the client: the item plus how many the run is holding.</summary>
/// <remarks><see cref="RestoresPpAllMoves"/> lets the bag menu tell a whole-moveset PP restore (Elixir/Max
/// Elixir — use directly) from a single-move one (Ether/Max Ether — needs a move-slot pick) without
/// re-deriving it from the item name. <see cref="UsableInBattle"/> is the server's verdict (from the
/// <see cref="ItemEffects"/> registry) on whether using the item now would do anything, so the client
/// filters the menu on a flag instead of re-encoding the category→effect mapping.</remarks>
public sealed record BagItemView(
    int Id,
    string Name,
    string Category,
    int Quantity,
    string Description,
    bool RestoresPpAllMoves,
    bool UsableInBattle
);

/// <summary>A running battle plus the connection currently bound to it and its abandon timer.</summary>
sealed class ActiveBattle
{
    public SignalRInput Input { get; } = new();
    public volatile string? CurrentConnectionId;

    // The run's STARTER, captured at claim — a fallback for the overview snapshot when no party is wired. NOT
    // necessarily the creature on the field: read that through GameSessionManager.ActiveCreature.
    public Creature? Player;

    // The same instance RunDirector's RunState plays over, so the party-hydrate endpoint reads the live roster.
    public Party? Party;

    public Bag? Bag;
    public Wallet? Wallet;
    public IReadOnlyDictionary<int, Item> ItemsById = new Dictionary<int, Item>();

    // Carried from the claimed PendingSession so on-demand REST reads report the run's generation, not a
    // hardcoded 1. Fixed for the whole run.
    public Generation Generation;

    // The run's ONE emitter (resolves the current connection per event, so it needs no rebinding on reconnect).
    // Kept single on purpose: a second emitter would silently diverge the moment it gains any state. Typed
    // concretely (not IBattleEventEmitter) so the reconnect branch can call the web-layer-only
    // ReplayLastKnownState (ARCHITECTURE.md §2.7) — GameSessionManager is the one place that constructs and
    // reads this field, so there's no abstraction to preserve here (the interface still exists for the engine's
    // own consumers, e.g. AttackAction, which never see this type).
    public SignalRBattleEventEmitter? Emitter;

    private readonly object _lock = new();
    private CancellationTokenSource? _abandonCts;

    /// <summary>Arm a timer that abandons the battle after <paramref name="grace"/> unless a reconnect cancels it.</summary>
    public void ScheduleAbandon(TimeSpan grace)
    {
        lock (_lock)
        {
            _abandonCts?.Cancel();
            var cts = new CancellationTokenSource();
            _abandonCts = cts;
            _ = Task.Delay(grace, cts.Token)
                .ContinueWith(
                    t =>
                    {
                        if (!t.IsCanceled)
                            Input.Cancel(); // grace expired → unblock the battle loop
                    },
                    TaskScheduler.Default
                );
        }
    }

    public void CancelAbandon()
    {
        lock (_lock)
        {
            _abandonCts?.Cancel();
            _abandonCts = null;
        }
    }
}
