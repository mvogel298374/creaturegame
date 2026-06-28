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

    public string RegisterSession(
        Creature player,
        IReadOnlyList<Attack> allMoves,
        Bag bag,
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
            Bag = session.Bag,
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
            (p, depth, biome) =>
                encounters.CreateEnemyAsync(
                    p,
                    session.AllMoves,
                    session.Rng,
                    biome: biome,
                    depth: depth
                ),
            Gen1TypeChart.Instance,
            battle.Input,
            new AiBattleInput(new Gen1TrainerAi(rng: session.Rng)),
            movePool: session.AllMoves,
            emitter: emitter,
            rng: session.Rng,
            // Between encounters, resolve any pending evolution against the DB (edges → IEvolutionRules →
            // evolved species + learnset). The runner applies it; the data concern stays in the web layer.
            checkEvolution: p => encounters.ResolvePlayerEvolutionAsync(p, session.AllMoves),
            // The run's bag, threaded into every Battle's player side; consumed items stay gone across the chain.
            playerBag: session.Bag,
            // Biome mode: the run charts a route through this region's playable biomes (the map screen). A
            // non-empty set flips the director from the legacy endless chain to biome traversal; the chosen
            // biome themes each encounter. ENCOUNTER_DESIGN.md §7 Phase 3b-2.
            playableBiomes: session.PlayableBiomes
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
            return battle.Player;
        if (_pending.TryGetValue(gameId, out var pending))
            return pending.Player;
        return null;
    }

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
        return battle
            .Bag.Entries.Where(e => e.Value > 0 && battle.ItemsById.ContainsKey(e.Key))
            .Select(e =>
            {
                var item = battle.ItemsById[e.Key];
                return new BagItemView(
                    item.Id,
                    item.Name ?? "",
                    item.Category.ToString(),
                    e.Value,
                    item.Description ?? "",
                    item.RestoresPpAllMoves
                );
            })
            .OrderBy(v => v.Id)
            .ToList();
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
    IReadOnlyList<Item> AllItems,
    IRandomSource Rng,
    IReadOnlyList<BiomeDefinition> PlayableBiomes,
    DateTimeOffset RegisteredAt
);

/// <summary>A bag entry for the client: the item plus how many the run is holding.</summary>
/// <remarks><see cref="RestoresPpAllMoves"/> lets the bag menu tell a whole-moveset PP restore (Elixir/Max
/// Elixir — use directly) from a single-move one (Ether/Max Ether — needs a move-slot pick) without
/// re-deriving it from the item name.</remarks>
public sealed record BagItemView(
    int Id,
    string Name,
    string Category,
    int Quantity,
    string Description,
    bool RestoresPpAllMoves
);

/// <summary>A running battle plus the connection currently bound to it and its abandon timer.</summary>
sealed class ActiveBattle
{
    public SignalRInput Input { get; } = new();
    public volatile string? CurrentConnectionId;

    // The persistent player creature for this run, for the on-demand overview snapshot (read-only display use).
    public Creature? Player;

    // The run's bag (threaded into every Battle) and the item catalog used to resolve a UseItem and render
    // the bag. Set when the session is claimed; never reassigned.
    public Bag? Bag;
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
