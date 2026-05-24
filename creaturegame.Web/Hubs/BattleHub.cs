using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Web.Hubs;

public class BattleHub : Hub
{
    public async Task ChooseMove(int moveIndex)
    {
        // Phase 6: resolve move against the GameSession for this connection
        await Task.CompletedTask;
    }
}
