using creaturegame.Attacks;
using creaturegame.Combat;

namespace creaturegame.Web.Battle;

public sealed class SignalRInput : IBattleInput
{
    private volatile TaskCompletionSource<int>? _tcs;

    public async Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tcs = tcs;
        var index = await tcs.Task;
        _tcs = null;

        var moves = context.Attacker.MoveSet;
        if (index >= 0 && index < moves.Count && moves[index].PowerPointsCurrent > 0)
            return moves[index];

        return moves.FirstOrDefault(m => m.PowerPointsCurrent > 0)
            ?? throw new InvalidOperationException($"{context.Attacker.Name}: no moves available");
    }

    public void SetChoice(int index)
    {
        var tcs = _tcs;
        tcs?.TrySetResult(index);
    }
}