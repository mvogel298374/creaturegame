using creaturegame.Web.Battle;
using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Web.Hubs;

public class BattleHub(GameSessionManager manager) : Hub<IBattleClient>
{
    public override async Task OnConnectedAsync()
    {
        // Same gameId on a later connection = a reconnect; AttachConnection handles both
        // the first-connect (start the battle) and reconnect (rebind) cases.
        var gameId = Context.GetHttpContext()?.Request.Query["gameId"].ToString();
        if (!string.IsNullOrEmpty(gameId))
            manager.AttachConnection(gameId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public Task ChooseMove(int moveIndex)
    {
        manager.SetMoveChoice(Context.ConnectionId, moveIndex);
        return Task.CompletedTask;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Start the reconnect grace window; the battle is abandoned only if the client
        // doesn't come back in time (prevents both a leak and killing a transient drop).
        manager.DetachConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
