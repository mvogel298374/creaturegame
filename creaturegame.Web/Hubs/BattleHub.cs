using creaturegame.Web.Battle;
using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Web.Hubs;

/// <summary>
/// The client's RPC surface for a run. Every method below follows the same shape: fire-and-forget completion
/// of a <see cref="GameSessionManager"/> input TCS the run/battle loop is blocked on — so each is a one-line
/// forward, and only what's unique to the prompt (params, tolerance for a bad answer) is worth documenting per
/// method. A malformed/out-of-range answer is never fatal — the engine falls back rather than stranding the
/// turn (exact fallback noted per method where it isn't the obvious "first option").
/// </summary>
public class BattleHub(GameSessionManager manager) : Hub<IBattleClient>
{
    public override async Task OnConnectedAsync()
    {
        // Same gameId on a later connection = a reconnect; AttachConnection handles both
        // the first-connect (start the battle) and reconnect (rebind) cases.
        var gameId = Context.GetHttpContext()?.Request.Query["gameId"].ToString();
        if (string.IsNullOrEmpty(gameId) || !manager.AttachConnection(gameId, Context.ConnectionId))
            // Unknown/expired gameId (a stale resume attempt, or no gameId at all) — reject the connection
            // outright rather than leaving the client attached to nothing. Before this, AttachConnection's
            // no-op here left conn.start() resolve successfully with no battle ever bound, so the client sat
            // on "Connecting…" forever with no error — ARCHITECTURE.md §2.7.
            throw new HubException("Unknown or expired game session.");
        await base.OnConnectedAsync();
    }

    public Task ChooseMove(int moveIndex)
    {
        manager.SetMoveChoice(Context.ConnectionId, moveIndex);
        return Task.CompletedTask;
    }

    /// <summary>Uses a bag item this turn. <paramref name="targetMoveSlot"/> (0–3) is a single-move PP
    /// restore's target and <paramref name="targetPartySlot"/> a Revive's fainted-member target; both null for
    /// the ordinary self-targeting items. A no-effect use resolves as <c>ItemUseFailed</c> in the engine.
    /// </summary>
    public Task UseItem(int itemId, int? targetMoveSlot, int? targetPartySlot)
    {
        manager.SetItemChoice(Context.ConnectionId, itemId, targetMoveSlot, targetPartySlot);
        return Task.CompletedTask;
    }

    /// <summary>Answers a level-up move-replacement prompt: the move slot (0–3) to forget, or null to
    /// decline.</summary>
    public Task ForgetMove(int? slotIndex)
    {
        manager.SetForgetChoice(Context.ConnectionId, slotIndex);
        return Task.CompletedTask;
    }

    /// <summary>Answers a between-encounter Poké Center offer: true to heal, false to skip.</summary>
    public Task RespondRecovery(bool accept)
    {
        manager.SetRecoveryChoice(Context.ConnectionId, accept);
        return Task.CompletedTask;
    }

    /// <summary>Answers an evolution offer: true to evolve, false to cancel (Gen 1 B-cancel).</summary>
    public Task RespondEvolution(bool allow)
    {
        manager.SetEvolutionChoice(Context.ConnectionId, allow);
        return Task.CompletedTask;
    }

    /// <summary>Answers the map-screen route choice with the biome to enter next.</summary>
    public Task ChooseBiome(string biomeId)
    {
        manager.SetBiomeChoice(Context.ConnectionId, biomeId);
        return Task.CompletedTask;
    }

    /// <summary>Answers a reward-choice modal with the chosen option (item or gold bag).</summary>
    public Task ChooseReward(int index)
    {
        manager.SetRewardChoice(Context.ConnectionId, index);
        return Task.CompletedTask;
    }

    /// <summary>Buys the shop stock item at <paramref name="index"/>. The shop node loops, so the modal stays
    /// open for more buys; an out-of-range/unaffordable index is tolerated (a no-op that re-prompts).</summary>
    public Task BuyShopItem(int index)
    {
        // Fully-qualified: this hub method's name shadows the record type of the same name in the class scope.
        manager.SetShopAction(Context.ConnectionId, new creaturegame.Combat.BuyShopItem(index));
        return Task.CompletedTask;
    }

    /// <summary>Leaves the shop, advancing the run to the next node.</summary>
    public Task LeaveShop()
    {
        manager.SetShopAction(Context.ConnectionId, creaturegame.Combat.LeaveShop.Instance);
        return Task.CompletedTask;
    }

    /// <summary>Answers an acquisition offer (themed draft / boss catch): <paramref name="accept"/> false =
    /// decline; true with a null <paramref name="replaceSlot"/> = add to the party; true with a slot index =
    /// add by swapping out that member. A decline / unhonourable accept is a no-op (the roster is left
    /// unchanged). <paramref name="nickname"/> is the raw client text from the acquisition's nickname step
    /// (Creature Naming Stage B) — null on decline or a skipped/cancelled step; normalized downstream the same
    /// way as the starter path.</summary>
    public Task RespondAcquisition(bool accept, int? replaceSlot, string? nickname)
    {
        manager.SetAcquisitionDecision(
            Context.ConnectionId,
            new creaturegame.Combat.AcquisitionDecision(accept, replaceSlot, nickname)
        );
        return Task.CompletedTask;
    }

    /// <summary>Answers a between-biome lead choice with the party-member slot to lead into the next biome. An
    /// out-of-range/unchanged index keeps the current lead (a no-op).</summary>
    public Task ChooseLead(int index)
    {
        manager.SetLeadChoice(Context.ConnectionId, index);
        return Task.CompletedTask;
    }

    /// <summary>Answers a forced faint-switch (Phase 4 Stage 3) with the party-member slot to send in against
    /// the same enemy.</summary>
    public Task RespondSwitchIn(int index)
    {
        manager.SetSwitchInChoice(Context.ConnectionId, index);
        return Task.CompletedTask;
    }

    /// <summary>Voluntarily switches the active creature out this turn for the party member at
    /// <paramref name="index"/> — the in-battle SWITCH turn-action. An illegal pick falls back to FIGHT in the
    /// engine.</summary>
    public Task ChooseSwitch(int index)
    {
        manager.SetSwitchChoice(Context.ConnectionId, index);
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
