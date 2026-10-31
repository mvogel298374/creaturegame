using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>Turns a <see cref="MoveFailureAnnouncement"/> from the rules seam into the event it prints.</summary>
public static class MoveFailureEvents
{
    public static BattleEvent For(
        MoveFailureAnnouncement announcement,
        Creature source,
        Creature target,
        string moveName,
        UnaffectedCause cause
    ) =>
        announcement switch
        {
            MoveFailureAnnouncement.Missed => new MoveMissed(source.Name, source.Id, moveName),
            MoveFailureAnnouncement.DoesntAffect => new MoveHadNoEffect(
                target.Name,
                target.Id,
                moveName
            ),
            MoveFailureAnnouncement.Unaffected => new MoveUnaffected(
                target.Name,
                target.Id,
                moveName,
                cause
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(announcement), announcement, null),
        };
}
