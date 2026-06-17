using creaturegame.Attacks;

namespace creaturegame.Combat;

/// <summary>
/// Adapts an <see cref="IBattleAi"/> brain to the <see cref="IBattleInput"/> slot a battle side expects. It
/// is the plumbing: builds the list of moves the creature may legally use this turn (PP remaining, not
/// Disabled) and hands it to the brain to choose. Keeping this separate from the brain means any brain
/// (wild, trainer, gym, a future generation) plugs in unchanged, and the candidate-filtering rule lives in
/// exactly one place. The non-AI inputs (player <c>SignalRInput</c>, <c>ScriptedInput</c>, the level-up /
/// recovery prompts) keep the <see cref="IBattleInput"/> defaults.
/// </summary>
public sealed class AiBattleInput(IBattleAi ai) : IBattleInput
{
    private readonly IBattleAi _ai = ai;

    public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
    {
        // Battle guarantees this is only called when CanSelectAnyMove == true, so at least one move has PP
        // and isn't Disabled.
        var candidates = context
            .Attacker.MoveSet.Where(m => m.PowerPointsCurrent > 0 && m != context.DisabledMove)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"{context.Attacker.Name}: ChooseMoveAsync called with no selectable move. "
                    + "Battle should have bypassed IBattleInput and passed null (Struggle) directly."
            );

        return Task.FromResult(_ai.ChooseMove(candidates, context));
    }
}
