using System.Collections.Concurrent;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
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

    // After a disconnect we wait this long for the client to reconnect before abandoning
    // the battle — covers the JS client's automatic-reconnect policy (gives up ~30 s).
    private static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(40);

    // Roguelite run-balance rules for the web run — the tunable "game rules" bag, separate from the Gen-1 seam
    // (see RunRules). The XP curve is a soft level-aware ramp: ~1.5× at low levels (leveling is already brisk
    // there under Gen-1's cheap early thresholds, so we barely nudge it — no sharp multi-level jumps) climbing
    // to 4.5× near the cap (where the Gen-1 grind is glacial). Around the default level-50 start it lands ~3×
    // (2.98×), so a biome (~4–6 encounters) advances the creature roughly 0.8–1.5 levels instead of a slow
    // crawl. These two anchors are the dials (trivially exposable as sliders); provisional, expected to be
    // retuned by playtesting. A deliberate, documented deviation from strict Gen-1 XP — see GENERATION_SEAMS.md
    // — kept out of the Gen-1 seam (Battle scales the seam's result; the formula itself is untouched).
    private static readonly RunRules RunTuning = new()
    {
        XpMultiplierEarly = 1.5,
        XpMultiplierLate = 4.5,
        // Innate party Exp-Share: each living bench member earns 50% of the lead's award off every win, so a
        // drafted roster keeps pace and stays swappable between biomes. Provisional — expected to be retuned by
        // playtesting (0 = off, 1 = full XP to all). A roguelite dial, kept out of the Gen-1 seam (see RunRules).
        BenchXpShare = 0.5,
    };

    public string RegisterSession(
        Creature player,
        IReadOnlyList<Attack> allMoves,
        Bag bag,
        Wallet wallet,
        IReadOnlyList<Item> allItems,
        IRandomSource rng,
        IReadOnlyList<BiomeDefinition> playableBiomes
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
    /// </summary>
    public void AttachConnection(string gameId, string connectionId)
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
            return;
        }

        // First connection: claim the pending session and start the battle loop.
        if (!_pending.TryRemove(gameId, out var session))
            return; // unknown or already-consumed gameId

        var battle = new ActiveBattle
        {
            CurrentConnectionId = connectionId,
            Player = session.Player,
            // The run's party — its Lead is the persistent player. The session owns this single instance so the
            // party-hydrate endpoint (GetParty) and the RunDirector's RunState read the same roster; the themed
            // draft (below) deposits acquired creatures into it.
            Party = new Party(session.Player),
            Bag = session.Bag,
            Wallet = session.Wallet,
            ItemsById = session.AllItems.ToDictionary(i => i.Id),
        };
        _active[gameId] = battle;
        _connToGame[connectionId] = gameId;

        // Emitter resolves the current connection per-event, so output follows reconnects.
        var emitter = new SignalRBattleEventEmitter(hubContext, () => battle.CurrentConnectionId);
        // Endless chain: one persistent player, a fresh DB-built enemy per encounter. A single enemy input
        // is reused across encounters (the AI is stateless per turn — it scores from the live TurnContext).
        // The enemy now thinks with Gen1TrainerAi: an intelligent-but-fallible Gen 1 move selector (scores
        // moves, then picks probabilistically so it usually plays the strong move but keeps some RBY
        // bad-decision flavour) instead of the old uniform-random RandomMoveInput.
        //
        // The run's single seeded RNG threads through every nondeterministic step — enemy construction
        // (species/level/DVs/moves), the battle rolls, and the AI's probabilistic move pick — so the whole
        // run replays from its seed (held as session.Rng). It's safe to share one instance: the run is single-threaded and
        // draws sequentially on this task.
        var runner = new RunDirector(
            session.Player,
            (p, depth, biome, tier) =>
                encounters.CreateEnemyAsync(
                    p,
                    session.AllMoves,
                    session.Rng,
                    biome: biome,
                    depth: depth,
                    // Node-derived tier (3c-1): the director passes a generation-agnostic EncounterTier per
                    // node; the web layer maps it to a concrete archetype (Elite→Strong, Boss→Boss). A plain
                    // Normal wild encounter rolls Weak vs Medium on the run RNG so wild fights vary in strength
                    // while presenting identically (ENCOUNTER_DESIGN.md §3.1).
                    archetype: EnemyArchetypes.For(tier, session.Rng)
                ),
            Gen1TypeChart.Instance,
            battle.Input,
            new AiBattleInput(new Gen1TrainerAi(rng: session.Rng)),
            movePool: session.AllMoves,
            new RunDirectorOptions
            {
                Emitter = emitter,
                Rng = session.Rng,
                // Between encounters, resolve any pending evolution against the DB (edges → IEvolutionRules →
                // evolved species + learnset). The runner applies it; the data concern stays in the web layer.
                CheckEvolution = p => encounters.ResolvePlayerEvolutionAsync(p, session.AllMoves),
                // The run's bag, threaded into every Battle's player side; consumed items stay gone across the chain.
                PlayerBag = session.Bag,
                // Biome mode: the run charts a route through this region's playable biomes (the map screen). A
                // non-empty set flips the director from the legacy endless chain to biome traversal; the chosen
                // biome themes each encounter. ENCOUNTER_DESIGN.md §7 Phase 3b-2.
                PlayableBiomes = session.PlayableBiomes,
                // Run Economy: the wallet battle wins and Treasure/Mystery credit, and the reward policy
                // (drop rates / gold curve / item eligibility) closed over this run's item catalog. The client
                // now answers the Treasure/Mystery reward ack (Phase C), so those nodes run at their full core
                // distribution (no node-plan gate).
                Wallet = session.Wallet,
                RewardSupplier = EncounterFactory.BuildRewardSupplier(session.AllItems),
                // Shop nodes spend the same wallet: run-scaled stock + prices closed over this run's item catalog.
                ShopSupplier = EncounterFactory.BuildShopSupplier(session.AllItems),
                // A Shop only rolls into a biome when the player can afford the cheapest possible item — so a broke
                // player (e.g. the opening node with a 0₽ wallet) never gets a dead, all-unaffordable shop.
                MinShopBudget = ShopCalculator.MinItemPrice,
                // Roguelite run-balance rules (the level-aware XP curve, boosted above pure Gen-1) — see RunTuning.
                RunRules = RunTuning,
                // Party threading: the RunDirector plays the run over this same party instance (its Lead is the
                // active player), so the party-hydrate endpoint and the roster panel read the live roster.
                Party = battle.Party,
                // Themed-draft acquisition (ENCOUNTER_DESIGN.md §4): rolled after every win, gated by cadence × n% ×
                // the fought-only pool (DraftCalculator), building the offered creature from this run's move pool +
                // DB. Deposits accepted creatures into the party above.
                DraftSupplier = encounters.BuildDraftSupplier(session.AllMoves),
                // Boss-catch acquisition (ENCOUNTER_DESIGN.md §4 Stage 2): rolled after a Boss win only, a small n%
                // chance (BossCatchCalculator) to add the defeated boss — built as a fresh full-HP copy of its species
                // at the boss's level. The win reward/XP is already applied, so the catch is pure upside.
                BossCatchSupplier = encounters.BuildBossCatchSupplier(session.AllMoves),
            }
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

    /// <summary>The creature the panel should describe: the party's <em>live</em> lead when a party is wired,
    /// else the session's starter. The lead moves — the between-biome lead swap (Phase 4 Stage 1d) reassigns it,
    /// and since the forced faint-switch (Stage 3) so does a mid-battle send-in — so it must be resolved per read,
    /// never captured at session claim. Pure + internal so the rule is unit-testable on its own (as
    /// <c>ProjectBagView</c> is).</summary>
    internal static Creature? ActiveCreature(Party? party, Creature? starter) =>
        party?.Lead ?? starter;

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
    public void SetItemChoice(string connectionId, int itemId, int? targetMoveSlot)
    {
        if (
            !_connToGame.TryGetValue(connectionId, out var gameId)
            || !_active.TryGetValue(gameId, out var battle)
        )
            return;

        if (battle.ItemsById.TryGetValue(itemId, out var item))
        {
            battle.Input.SetItemChoice(item, targetMoveSlot);
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
        return ProjectBagView(battle.Bag, battle.ItemsById);
    }

    /// <summary>Projects the held bag (id → qty) plus the item catalog into the client's <see cref="BagItemView"/>
    /// list, ordered by id. Pure (no session state) so the wire projection — notably the
    /// <see cref="BagItemView.UsableInBattle"/> flag — is unit-testable without standing up a live battle.</summary>
    internal static IReadOnlyList<BagItemView> ProjectBagView(
        Bag bag,
        IReadOnlyDictionary<int, Item> itemsById
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
                    // Single source of truth for "does anything in battle": the engine's effect registry.
                    // Ball/Revive/Other have no effect yet ⇒ null ⇒ the bag menu hides them, so this can't
                    // drift out of lockstep with ItemEffects the way a client-side category list would.
                    ItemEffects.For(item.Category)
                        is not null
                );
            })
            .OrderBy(v => v.Id)
            .ToList();

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
    DateTimeOffset RegisteredAt
);

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

    // The run's STARTER, captured at claim and never reassigned — a fallback for the on-demand overview snapshot
    // when no party is wired. It is NOT necessarily the creature now on the field: read the active one through
    // GameSessionManager.ActiveCreature, which prefers the party's live Lead.
    public Creature? Player;

    // The run's party (up to six). The same instance the RunDirector's RunState plays over, so the party-hydrate
    // endpoint reads the live roster — and its Lead is the creature actually on the field (the between-biome lead
    // swap and the forced faint-switch both move it). Set when the session is claimed; never reassigned.
    public Party? Party;

    // The run's bag (threaded into every Battle), wallet (credited by reward rolls), and the item catalog
    // used to resolve a UseItem and render the bag. Set when the session is claimed; never reassigned.
    public Bag? Bag;
    public Wallet? Wallet;
    public IReadOnlyDictionary<int, Item> ItemsById = new Dictionary<int, Item>();

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
