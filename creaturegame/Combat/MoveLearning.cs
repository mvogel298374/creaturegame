using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// The move-learning flow, shared by <see cref="Battle"/> (a level gained mid-fight), <see cref="RunDirector"/>
/// (the evolved form's moves, between encounters), and the TM/HM move-teach reward (a picked
/// <see cref="MoveTeachRewardOption"/>, outside battle). A free slot auto-learns; a full moveset emits
/// <see cref="MoveReplacementRequired"/> and blocks on the player's input — a chosen slot (0–3) is replaced,
/// <c>null</c> declines (canonical Gen 1 "don't learn"). Only the player ever learns, so it always uses the
/// player input. <see cref="TeachMoveAsync"/> is the one-move primitive; <see cref="LearnMovesForLevelAsync"/>
/// is a level-up batch built on top of it, so every caller runs the exact same auto-learn / replacement-prompt
/// sequence.
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
            await TeachMoveAsync(learner, move, emitter, playerInput);
    }

    /// <summary>Teaches a single, already-resolved <paramref name="move"/> to <paramref name="learner"/> — the
    /// primitive both the level-up loop above and the TM/HM move-teach reward (<c>RewardResolution</c>) call.
    /// Not gated on legality here: the caller (level-up learnset lookup, or the reward's own
    /// <c>MoveTeachRewardOption.AbleBySlot</c> check) already decided this move is legal for this creature.</summary>
    public static async Task TeachMoveAsync(
        Creature learner,
        Attack move,
        IBattleEventEmitter? emitter,
        IBattleInput playerInput
    )
    {
        if (learner.AddAttack(move))
        {
            emitter?.Emit(new MoveLearned(learner.Name, move.Name ?? ""));
            return;
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
