using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// The level-up move-learning flow, shared by <see cref="Battle"/> (a level gained mid-fight) and
/// <see cref="BattleRunner"/> (the evolved form's moves, between encounters). A free slot auto-learns; a
/// full moveset emits <see cref="MoveReplacementRequired"/> and blocks on the player's input — a chosen
/// slot (0–3) is replaced, <c>null</c> declines (canonical Gen 1 "don't learn"). Only the player ever
/// learns, so it always uses the player input. Extracted so the evolution path reuses the exact same
/// auto-learn / replacement-prompt sequence as a normal level-up.
/// </summary>
internal static class MoveLearning
{
    public static async Task LearnMovesForLevelAsync(
        Creature learner,
        int level,
        IBattleEventEmitter? emitter,
        IBattleInput playerInput
    )
    {
        foreach (var move in learner.MovesLearnedAtLevel(level).ToList())
        {
            if (learner.AddAttack(move))
            {
                emitter?.Emit(new MoveLearned(learner.Name, move.Name ?? ""));
                continue;
            }

            // Four slots full — ask the player which move to forget (or to decline).
            emitter?.Emit(
                new MoveReplacementRequired(
                    learner.Name,
                    move.Name ?? "",
                    learner.MoveSet.Select(m => m.Base.Name ?? "").ToList()
                )
            );
            int? slot = await playerInput.ChooseMoveToForgetAsync(
                new MoveReplacementContext(learner, move)
            );
            if (slot is int s && s >= 0 && s < learner.MoveSet.Count)
            {
                string forgotten = learner.MoveSet[s].Base.Name ?? "";
                learner.ReplaceMove(s, move);
                emitter?.Emit(new MoveForgotten(learner.Name, forgotten));
                emitter?.Emit(new MoveLearned(learner.Name, move.Name ?? ""));
            }
            else
            {
                // null / out of range → declined: the moveset is unchanged.
                emitter?.Emit(new MoveLearnDeclined(learner.Name, move.Name ?? ""));
            }
        }
    }
}
