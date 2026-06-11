using creaturegame.Attacks;
using creaturegame.Combat;

namespace creaturegame.Web.Battle;

public sealed class SignalRInput : IBattleInput
{
    private volatile TaskCompletionSource<int>? _tcs;
    private volatile TaskCompletionSource<int?>? _forgetTcs;
    private volatile TaskCompletionSource<bool>? _recoveryTcs;
    private volatile bool _cancelled;

    public async Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
    {
        // Disconnect may land while it's the enemy's turn (no pending _tcs); the
        // flag makes the *next* player turn throw immediately instead of hanging.
        if (_cancelled)
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tcs = tcs;
        var index = await tcs.Task; // throws OperationCanceledException if Cancel() ran
        _tcs = null;

        var moves = context.Attacker.MoveSet;
        if (
            index >= 0
            && index < moves.Count
            && moves[index].PowerPointsCurrent > 0
            && moves[index] != context.DisabledMove
        )
            return moves[index];

        // Fallback (out-of-range / a Disabled or PP-less slot slipped through): first selectable move.
        return moves.FirstOrDefault(m => m.PowerPointsCurrent > 0 && m != context.DisabledMove)
            ?? throw new InvalidOperationException($"{context.Attacker.Name}: no moves available");
    }

    public void SetChoice(int index)
    {
        var tcs = _tcs;
        tcs?.TrySetResult(index);
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
    /// Unblocks a battle loop waiting on player input when the client disconnects,
    /// so the fire-and-forget battle task can complete and be collected.
    /// </summary>
    public void Cancel()
    {
        _cancelled = true;
        _tcs?.TrySetCanceled();
        _forgetTcs?.TrySetCanceled();
        _recoveryTcs?.TrySetCanceled();
    }
}
