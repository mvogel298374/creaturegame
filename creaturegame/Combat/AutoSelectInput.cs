using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Default move selector — picks the first available move with remaining PP.
/// Replicates the original hardcoded behaviour until real input sources are
/// implemented (ConsoleInput at Priority 6, AI variants at Priority 9).
///
/// Use AutoSelectInput.Instance for both sides when constructing Battle in tests/debug.
/// </summary>
public sealed class AutoSelectInput : IBattleInput
{
    public static readonly AutoSelectInput Instance = new();
    private AutoSelectInput() { }

    public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
    {
        // Battle guarantees this is only called when IsOutOfPP == false.
        var move = context.Attacker.GetAvailableMove()
            ?? throw new InvalidOperationException(
                $"{context.Attacker.Name}: ChooseMoveAsync called with no PP remaining. " +
                "Battle should have bypassed IBattleInput and passed null (Struggle) directly.");

        return Task.FromResult(move);
    }
}
