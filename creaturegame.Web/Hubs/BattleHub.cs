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

    /// <summary>
    /// Uses a bag item this turn: <paramref name="itemId"/> is the item to use and
    /// <paramref name="targetMoveSlot"/> the move slot (0–3) a single-move PP restore targets (null
    /// otherwise). Mirrors <see cref="ChooseMove"/> — fire-and-forget completion of the turn handshake the
    /// battle loop is blocked on. An item that has no effect is resolved by the engine (`ItemUseFailed`).
    /// </summary>
    public Task UseItem(int itemId, int? targetMoveSlot)
    {
        manager.SetItemChoice(Context.ConnectionId, itemId, targetMoveSlot);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Answers a level-up move-replacement prompt: <paramref name="slotIndex"/> is the move (0–3) to forget
    /// and replace, or <c>null</c> to decline (SkipNewMove). Mirrors <see cref="ChooseMove"/> — fire-and-forget
    /// completion of the input TCS the battle loop is blocked on.
    /// </summary>
    public Task ForgetMove(int? slotIndex)
    {
        manager.SetForgetChoice(Context.ConnectionId, slotIndex);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Answers a between-encounter Poké Center recovery offer: <paramref name="accept"/> true to heal, false to
    /// skip. Mirrors <see cref="ForgetMove"/> — fire-and-forget completion of the input TCS the run loop is
    /// blocked on.
    /// </summary>
    public Task RespondRecovery(bool accept)
    {
        manager.SetRecoveryChoice(Context.ConnectionId, accept);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Answers an evolution offer: <paramref name="allow"/> true to evolve, false to cancel (Gen 1 B-cancel).
    /// Mirrors <see cref="RespondRecovery"/> — fire-and-forget completion of the input TCS the run loop is
    /// blocked on.
    /// </summary>
    public Task RespondEvolution(bool allow)
    {
        manager.SetEvolutionChoice(Context.ConnectionId, allow);
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
