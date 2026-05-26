using System.Collections.Concurrent;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using BattleEngine = creaturegame.Combat.Battle;

namespace creaturegame.Web.Battle;

public sealed class GameSessionManager(IHubContext<BattleHub, IBattleClient> hubContext)
{
    private readonly ConcurrentDictionary<string, PendingSession> _pending = new();

    public string RegisterSession(Creature player, Creature enemy)
    {
        var gameId = Guid.NewGuid().ToString("N");
        _pending[gameId] = new PendingSession(player, enemy);
        return gameId;
    }

    public Task StartBattleAsync(string gameId, string connectionId)
    {
        if (!_pending.TryRemove(gameId, out var session))
            return Task.CompletedTask;

        var emitter = new SignalRBattleEventEmitter(hubContext, connectionId);
        var battle  = new BattleEngine(
            session.Player, session.Enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance, AutoSelectInput.Instance,
            emitter: emitter);

        _ = Task.Run(() => battle.StartFightAsync());
        return Task.CompletedTask;
    }
}

sealed record PendingSession(Creature Player, Creature Enemy);