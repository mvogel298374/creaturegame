using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>What a strike leaves in a side's Substitute byte. See GENERATION_SEAMS.md §2.</summary>
public readonly record struct SubstituteStrike(int Remnant, bool Broke);

public static class SubstituteStrikes
{
    /// <summary>Strikes a standing decoy and its byte; true when the decoy broke.</summary>
    public static bool StrikeDecoy(BattleState state, int damage, IBattleRules rules)
    {
        state.SubstituteRemnant = rules
            .StrikeSubstituteByte(state.SubstituteRemnant, damage)
            .Remnant;
        state.SubstituteHp = Math.Max(0, state.SubstituteHp - damage);
        return state.SubstituteHp == 0;
    }

    /// <summary>Prints the lines for a strike on <paramref name="owner"/>'s Substitute.</summary>
    public static void AnnounceStrike(
        Creature owner,
        int hpAfter,
        bool broke,
        IBattleRules rules,
        IBattleEventEmitter? emitter
    )
    {
        emitter?.Emit(new SubstituteAbsorbedHit(owner.Name, owner.Id, hpAfter));
        if (broke)
            emitter?.Emit(BreakLine(rules.SubstituteBreakAnnouncement, owner));
    }

    public static BattleEvent FailureLine(
        SubstituteFailureAnnouncement announcement,
        Creature user,
        bool alreadyUp
    ) =>
        announcement switch
        {
            SubstituteFailureAnnouncement.NamedReason => alreadyUp
                ? new SubstituteAlreadyUp(user.Name, user.Id)
                : new SubstituteTooWeak(),
            SubstituteFailureAnnouncement.Failed => new MoveFailed(),
            _ => throw new ArgumentOutOfRangeException(nameof(announcement), announcement, null),
        };

    public static BattleEvent SetupLine(
        SubstituteSetupAnnouncement announcement,
        Creature user,
        int substituteHp
    ) =>
        announcement switch
        {
            SubstituteSetupAnnouncement.ItCreated => new SubstituteCreated(
                user.Name,
                user.Id,
                substituteHp
            ),
            SubstituteSetupAnnouncement.UserMade => new SubstituteMade(
                user.Name,
                user.Id,
                substituteHp
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(announcement), announcement, null),
        };

    private static BattleEvent BreakLine(
        SubstituteBreakAnnouncement announcement,
        Creature owner
    ) =>
        announcement switch
        {
            SubstituteBreakAnnouncement.TookDamageThenBroke => new SubstituteBroke(
                owner.Name,
                owner.Id
            ),
            SubstituteBreakAnnouncement.TookDamageThenFaded => new SubstituteFaded(
                owner.Name,
                owner.Id
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(announcement), announcement, null),
        };
}
