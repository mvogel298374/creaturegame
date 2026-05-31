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
    private readonly ConcurrentDictionary<string, PendingSession> _pending = new();
    private readonly ConcurrentDictionary<string, SignalRInput>   _inputs  = new();

    // Pending sessions that are never claimed (client never connected) are evicted after this TTL.
    private static readonly TimeSpan PendingSessionTtl = TimeSpan.FromMinutes(2);

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

    public Task StartBattleAsync(string gameId, string connectionId)
    {
        if (!_pending.TryRemove(gameId, out var session))
            return Task.CompletedTask;

        var playerInput = new SignalRInput();
        _inputs[connectionId] = playerInput;

        var emitter = new SignalRBattleEventEmitter(hubContext, connectionId);
        var battle  = new BattleEngine(
            session.Player, session.Enemy,
            Gen1TypeChart.Instance,
            playerInput, AutoSelectInput.Instance,
            movePool: session.AllMoves,
            emitter: emitter);

        _ = Task.Run(async () =>
        {
            try   { await battle.StartFightAsync(); }
            finally { _inputs.TryRemove(connectionId, out _); }
        });

        return Task.CompletedTask;
    }

    public void SetMoveChoice(string connectionId, int moveIndex)
    {
        if (_inputs.TryGetValue(connectionId, out var input))
            input.SetChoice(moveIndex);
    }
}

sealed record PendingSession(Creature Player, Creature Enemy, IReadOnlyList<Attack> AllMoves, DateTimeOffset RegisteredAt);