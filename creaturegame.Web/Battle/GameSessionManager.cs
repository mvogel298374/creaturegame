using System.Collections.Concurrent;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using BattleEngine = creaturegame.Combat.Battle;

namespace creaturegame.Web.Battle;

public sealed class GameSessionManager(IHubContext<BattleHub, IBattleClient> hubContext)
{
    private readonly ConcurrentDictionary<string, PendingSession> _pending    = new();  // gameId → registered, not yet started
    private readonly ConcurrentDictionary<string, ActiveBattle>   _active     = new();  // gameId → running battle
    private readonly ConcurrentDictionary<string, string>         _connToGame = new();  // connectionId → gameId (routing)

    // Pending sessions never claimed (client never connected) are evicted after this TTL.
    private static readonly TimeSpan PendingSessionTtl = TimeSpan.FromMinutes(2);

    // After a disconnect we wait this long for the client to reconnect before abandoning
    // the battle — covers the JS client's automatic-reconnect policy (gives up ~30 s).
    private static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(40);

    public string RegisterSession(Creature player, Creature enemy, IReadOnlyList<Attack> allMoves)
    {
        var gameId = Guid.NewGuid().ToString("N");
        _pending[gameId] = new PendingSession(player, enemy, allMoves, DateTimeOffset.UtcNow);
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
            return;  // unknown or already-consumed gameId

        var battle = new ActiveBattle { CurrentConnectionId = connectionId };
        _active[gameId] = battle;
        _connToGame[connectionId] = gameId;

        // Emitter resolves the current connection per-event, so output follows reconnects.
        var emitter = new SignalRBattleEventEmitter(hubContext, () => battle.CurrentConnectionId);
        var engine  = new BattleEngine(
            session.Player, session.Enemy,
            Gen1TypeChart.Instance,
            battle.Input, new RandomMoveInput(),
            movePool: session.AllMoves,
            emitter: emitter);

        _ = Task.Run(async () =>
        {
            try
            {
                await engine.StartFightAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when the reconnect grace expires — see DetachConnection.
                Console.WriteLine($"[GameSessionManager] Battle {gameId} abandoned (client did not reconnect).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameSessionManager] Battle {gameId} failed: {ex}");
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

    public void SetMoveChoice(string connectionId, int moveIndex)
    {
        if (_connToGame.TryGetValue(connectionId, out var gameId)
            && _active.TryGetValue(gameId, out var battle))
            battle.Input.SetChoice(moveIndex);
    }

    /// <summary>
    /// Called when a connection drops. If it is the battle's current connection, a grace
    /// timer is started; the battle is abandoned (its input cancelled so the loop ends and
    /// is collected) only if no reconnect arrives in time. A stale drop — an old connection
    /// dropping after we have already rebound to a newer one — is ignored.
    /// </summary>
    public void DetachConnection(string connectionId)
    {
        if (!_connToGame.TryGetValue(connectionId, out var gameId)) return;
        if (!_active.TryGetValue(gameId, out var battle)) return;
        if (battle.CurrentConnectionId != connectionId) return;  // stale old connection

        battle.ScheduleAbandon(ReconnectGrace);
    }
}

sealed record PendingSession(Creature Player, Creature Enemy, IReadOnlyList<Attack> AllMoves, DateTimeOffset RegisteredAt);

/// <summary>A running battle plus the connection currently bound to it and its abandon timer.</summary>
sealed class ActiveBattle
{
    public SignalRInput Input { get; } = new();
    public volatile string? CurrentConnectionId;

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
            _ = Task.Delay(grace, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled) Input.Cancel();   // grace expired → unblock the battle loop
            }, TaskScheduler.Default);
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
