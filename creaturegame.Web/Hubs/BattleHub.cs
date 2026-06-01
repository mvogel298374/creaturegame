using creaturegame.Web.Battle;
using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Web.Hubs;

public class BattleHub(GameSessionManager manager) : Hub<IBattleClient>
{
    public override async Task OnConnectedAsync()
    {
        var gameId = Context.GetHttpContext()?.Request.Query["gameId"].ToString();
        if (!string.IsNullOrEmpty(gameId))
            await manager.StartBattleAsync(gameId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public Task ChooseMove(int moveIndex)
    {
        manager.SetMoveChoice(Context.ConnectionId, moveIndex);
        return Task.CompletedTask;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Unblock any battle loop waiting on this connection's input so its
        // fire-and-forget task can complete and be collected (prevents leaks).
        manager.AbandonBattle(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
