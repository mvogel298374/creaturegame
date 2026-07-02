using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Items;

namespace creaturegame.Web.Battle;

public sealed class SignalRInput : IBattleInput
{
    // One pending turn choice — the player picks either a move (FIGHT) or a bag item (ITEM); both
    // complete this same TCS, so the battle loop blocks on a single handshake per turn.
    private volatile TaskCompletionSource<TurnRequest>? _turnTcs;
    private volatile TaskCompletionSource<int?>? _forgetTcs;
    private volatile TaskCompletionSource<bool>? _recoveryTcs;
    private volatile TaskCompletionSource<bool>? _evolutionTcs;
    private volatile TaskCompletionSource<string>? _biomeTcs;
    private volatile TaskCompletionSource<bool>? _rewardAckTcs;
    private volatile bool _cancelled;

    // The raw request the hub completes the turn handshake with, mapped to a TurnChoice below.
    private abstract record TurnRequest;

    private sealed record MoveRequest(int Index) : TurnRequest;

    private sealed record ItemRequest(Item Item, int? TargetMoveSlot) : TurnRequest;

    /// <summary>
    /// The player's whole-turn choice: FIGHT (a move) or ITEM (a bag item). Overrides the default
    /// (move-only) so the interactive player can use the bag; the hub completes the handshake via
    /// <see cref="SetChoice"/> or <see cref="SetItemChoice"/>.
    /// </summary>
    public async Task<TurnChoice> ChooseTurnActionAsync(TurnContext context)
    {
        // Disconnect may land while it's the enemy's turn (no pending TCS); the flag makes the *next*
        // player turn throw immediately instead of hanging.
        if (_cancelled)
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");

        var tcs = new TaskCompletionSource<TurnRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _turnTcs = tcs;
        var request = await tcs.Task; // throws OperationCanceledException if Cancel() ran
        _turnTcs = null;

        return request switch
        {
            ItemRequest item => new ItemTurnChoice(item.Item, item.TargetMoveSlot),
            MoveRequest move => new MoveTurnChoice(ResolveMove(context, move.Index)),
            _ => new MoveTurnChoice(ResolveMove(context, -1)),
        };
    }

    // FIGHT only — kept so the interface contract holds; never returns an item to a move-only caller.
    public async Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
    {
        var choice = await ChooseTurnActionAsync(context);
        return choice is MoveTurnChoice m ? m.Move : ResolveMove(context, -1);
    }

    // Maps a chosen slot index to a usable move, with the same validation as before: in range, has PP,
    // not Disabled — else fall back to the first selectable move (an out-of-range -1 always falls back).
    private static PokemonAttack ResolveMove(TurnContext context, int index)
    {
        var moves = context.Attacker.MoveSet;
        if (
            index >= 0
            && index < moves.Count
            && moves[index].PowerPointsCurrent > 0
            && moves[index] != context.DisabledMove
        )
            return moves[index];

        return moves.FirstOrDefault(m => m.PowerPointsCurrent > 0 && m != context.DisabledMove)
            ?? throw new InvalidOperationException($"{context.Attacker.Name}: no moves available");
    }

    public void SetChoice(int index)
    {
        var tcs = _turnTcs;
        tcs?.TrySetResult(new MoveRequest(index));
    }

    /// <summary>Completes the turn handshake with a bag-item choice (the resolved <see cref="Item"/> and,
    /// for a single-move PP restore, the target move slot). Routed from <c>BattleHub.UseItem</c>.</summary>
    public void SetItemChoice(Item item, int? targetMoveSlot)
    {
        var tcs = _turnTcs;
        tcs?.TrySetResult(new ItemRequest(item, targetMoveSlot));
    }

    /// <summary>
    /// Awaits the player's level-up replace-move decision: the slot index (0–3) to forget, or <c>null</c> to
    /// decline. Mirrors <see cref="ChooseMoveAsync"/>'s TCS handshake (the hub's <c>ForgetMove</c> completes it
    /// via <see cref="SetForgetChoice"/>); the <see cref="_cancelled"/> guard makes a disconnect throw rather
    /// than hang the loop on the prompt.
    /// </summary>
    public async Task<int?> ChooseMoveToForgetAsync(MoveReplacementContext context)
    {
        if (_cancelled)
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");

        var tcs = new TaskCompletionSource<int?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _forgetTcs = tcs;
        var slot = await tcs.Task; // throws OperationCanceledException if Cancel() ran
        _forgetTcs = null;
        return slot;
    }

    public void SetForgetChoice(int? slot)
    {
        var tcs = _forgetTcs;
        tcs?.TrySetResult(slot);
    }

    /// <summary>
    /// Awaits the player's between-encounter Poké Center decision: <c>true</c> to heal, <c>false</c> to skip.
    /// Same TCS handshake as the other prompts (the hub's <c>RespondRecovery</c> completes it via
    /// <see cref="SetRecoveryChoice"/>); the <see cref="_cancelled"/> guard makes a disconnect throw rather
    /// than hang the run on the prompt.
    /// </summary>
    public async Task<bool> ConfirmRecoveryAsync(RecoveryContext context)
    {
        if (_cancelled)
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _recoveryTcs = tcs;
        var accept = await tcs.Task; // throws OperationCanceledException if Cancel() ran
        _recoveryTcs = null;
        return accept;
    }

    public void SetRecoveryChoice(bool accept)
    {
        var tcs = _recoveryTcs;
        tcs?.TrySetResult(accept);
    }

    /// <summary>
    /// Awaits the player's evolution allow/cancel decision: <c>true</c> to evolve, <c>false</c> to cancel.
    /// Same TCS handshake as the other prompts (the hub's <c>RespondEvolution</c> completes it via
    /// <see cref="SetEvolutionChoice"/>); the <see cref="_cancelled"/> guard makes a disconnect throw rather
    /// than hang the run on the prompt.
    /// </summary>
    public async Task<bool> ConfirmEvolutionAsync(EvolutionPromptContext context)
    {
        if (_cancelled)
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _evolutionTcs = tcs;
        var allow = await tcs.Task; // throws OperationCanceledException if Cancel() ran
        _evolutionTcs = null;
        return allow;
    }

    public void SetEvolutionChoice(bool allow)
    {
        var tcs = _evolutionTcs;
        tcs?.TrySetResult(allow);
    }

    /// <summary>
    /// Awaits the player's route choice on the map screen: the <see cref="BiomeDefinition.Id"/> of the biome to
    /// enter next. Same TCS handshake as the other prompts (the hub's <c>ChooseBiome</c> completes it via
    /// <see cref="SetBiomeChoice"/>); the <see cref="_cancelled"/> guard makes a disconnect throw rather than
    /// hang the run on the map. An unknown / stale id is tolerated downstream — the run loop's
    /// <c>BiomeChoiceEvent</c> falls back to the first offered biome.
    /// </summary>
    public async Task<string> ChooseBiomeAsync(BiomeChoiceContext context)
    {
        if (_cancelled)
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");

        var tcs = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _biomeTcs = tcs;
        var biomeId = await tcs.Task; // throws OperationCanceledException if Cancel() ran
        _biomeTcs = null;
        return biomeId;
    }

    public void SetBiomeChoice(string biomeId)
    {
        var tcs = _biomeTcs;
        tcs?.TrySetResult(biomeId);
    }

    /// <summary>
    /// Awaits the player's dismissal of a Treasure/Mystery reward modal. Same TCS handshake as the other
    /// prompts (the hub's <c>AcknowledgeReward</c> completes it via <see cref="SetRewardAck"/>); the
    /// <see cref="_cancelled"/> guard makes a disconnect throw rather than hang the run on the modal.
    /// </summary>
    public async Task AcknowledgeRewardAsync(RewardAckContext context)
    {
        if (_cancelled)
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _rewardAckTcs = tcs;
        await tcs.Task; // throws OperationCanceledException if Cancel() ran
        _rewardAckTcs = null;
    }

    public void SetRewardAck()
    {
        var tcs = _rewardAckTcs;
        tcs?.TrySetResult(true);
    }

    /// <summary>
    /// Unblocks a battle loop waiting on player input when the client disconnects,
    /// so the fire-and-forget battle task can complete and be collected.
    /// </summary>
    public void Cancel()
    {
        _cancelled = true;
        _turnTcs?.TrySetCanceled();
        _forgetTcs?.TrySetCanceled();
        _recoveryTcs?.TrySetCanceled();
        _evolutionTcs?.TrySetCanceled();
        _biomeTcs?.TrySetCanceled();
        _rewardAckTcs?.TrySetCanceled();
    }
}
