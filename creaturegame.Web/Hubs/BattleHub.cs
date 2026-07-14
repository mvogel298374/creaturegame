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

    /// <summary>
    /// Answers the map-screen route choice: <paramref name="biomeId"/> is the biome to enter next. Mirrors
    /// <see cref="RespondRecovery"/> — fire-and-forget completion of the input TCS the run loop is blocked on.
    /// An unknown id is tolerated by the run loop (falls back to the first offered biome).
    /// </summary>
    public Task ChooseBiome(string biomeId)
    {
        manager.SetBiomeChoice(Context.ConnectionId, biomeId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Answers a reward-choice modal: <paramref name="index"/> is the chosen option (item or gold bag). Mirrors
    /// <see cref="RespondRecovery"/> — fire-and-forget completion of the input TCS the run loop is blocked on.
    /// An out-of-range index is tolerated by the run loop (falls back to the first option).
    /// </summary>
    public Task ChooseReward(int index)
    {
        manager.SetRewardChoice(Context.ConnectionId, index);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Buys the shop stock item at <paramref name="index"/> — completes the shop input TCS the run loop is
    /// blocked on with a <see cref="BuyShopItem"/>. The shop node loops, so the modal stays open for more buys;
    /// an out-of-range / unaffordable index is tolerated (a no-op that re-prompts). Fire-and-forget, like the
    /// other prompt answers.
    /// </summary>
    public Task BuyShopItem(int index)
    {
        // Fully-qualified: this hub method's name shadows the record type of the same name in the class scope.
        manager.SetShopAction(Context.ConnectionId, new creaturegame.Combat.BuyShopItem(index));
        return Task.CompletedTask;
    }

    /// <summary>Leaves the shop — completes the shop input TCS with <c>LeaveShop</c> so the run advances to the
    /// next node.</summary>
    public Task LeaveShop()
    {
        manager.SetShopAction(Context.ConnectionId, creaturegame.Combat.LeaveShop.Instance);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Answers an acquisition offer (themed draft / boss catch): <paramref name="accept"/> false = decline;
    /// true with a null <paramref name="replaceSlot"/> = add to the party; true with a slot index = add by
    /// swapping out that member (the full-party path). Mirrors <see cref="RespondRecovery"/> — fire-and-forget
    /// completion of the input TCS the run loop is blocked on. A decline / unhonourable accept is a no-op in the
    /// run loop (the roster is left unchanged).
    /// </summary>
    public Task RespondAcquisition(bool accept, int? replaceSlot)
    {
        manager.SetAcquisitionDecision(
            Context.ConnectionId,
            new creaturegame.Combat.AcquisitionDecision(accept, replaceSlot)
        );
        return Task.CompletedTask;
    }

    /// <summary>
    /// Answers a between-biome lead choice: <paramref name="index"/> is the party-member slot to lead into the
    /// next biome. Mirrors <see cref="RespondRecovery"/> — fire-and-forget completion of the input TCS the run
    /// loop is blocked on. An out-of-range / unchanged index keeps the current lead (a no-op in the run loop).
    /// </summary>
    public Task ChooseLead(int index)
    {
        manager.SetLeadChoice(Context.ConnectionId, index);
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
