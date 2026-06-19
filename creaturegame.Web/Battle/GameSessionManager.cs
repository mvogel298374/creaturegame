using System.Collections.Concurrent;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
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
        IRandomSource rng,
        int seed
    )
    {
        var gameId = Guid.NewGuid().ToString("N");
        _pending[gameId] = new PendingSession(player, allMoves, rng, seed, DateTimeOffset.UtcNow);
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
        // run replays from session.Seed. It's safe to share one instance: the run is single-threaded and
        // draws sequentially on this task.
        var runner = new BattleRunner(
            session.Player,
            p => encounters.CreateEnemyAsync(p, session.AllMoves, session.Rng),
            Gen1TypeChart.Instance,
            battle.Input,
            new AiBattleInput(new Gen1TrainerAi(rng: session.Rng)),
            movePool: session.AllMoves,
            emitter: emitter,
            rng: session.Rng,
            // Between encounters, resolve any pending evolution against the DB (edges → IEvolutionRules →
            // evolved species + learnset). The runner applies it; the data concern stays in the web layer.
            checkEvolution: p => encounters.ResolvePlayerEvolutionAsync(p, session.AllMoves)
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
    /// A display-only read of live state — no lock (the battle thread only mutates scalar stat fields).
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
    IRandomSource Rng,
    int Seed,
    DateTimeOffset RegisteredAt
);

/// <summary>A running battle plus the connection currently bound to it and its abandon timer.</summary>
sealed class ActiveBattle
{
    public SignalRInput Input { get; } = new();
    public volatile string? CurrentConnectionId;

    // The persistent player creature for this run, for the on-demand overview snapshot (read-only display use).
    public Creature? Player;

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
