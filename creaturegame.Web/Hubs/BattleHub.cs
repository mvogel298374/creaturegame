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

    public async Task ChooseMove(int moveIndex)
    {
        // Phase 6: forward to SignalRInput for this connection
        await Task.CompletedTask;
    }
}
