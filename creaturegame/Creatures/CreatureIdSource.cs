namespace creaturegame.Creatures;

/// <summary>
/// Mints <see cref="Creature.Id"/>s for one run: a monotonic counter starting at 1 (0 is "unassigned"). Owned by
/// <see cref="Combat.RunState"/>, so ids are scoped to a run and reproduce for a seed — creatures enter the run
/// in a deterministic order, and a counter (unlike a Guid or a process-wide sequence) adds nothing random. Design
/// and the "why not a Guid" reasoning → <c>ARCHITECTURE.md</c> §2.2 (history: <c>docs/TODO_ARCHIVE.md</c> → <em>Creature Identity</em>). Single-threaded by
/// design, like the run loop that drives it.
/// </summary>
public sealed class CreatureIdSource
{
    private int _last;

    /// <summary>The highest id handed out so far (0 when none). What a future save layer persists so a resumed
    /// run keeps minting above every existing id.</summary>
    public int HighWater => _last;

    public int Next() => ++_last;

    /// <summary>Gives <paramref name="creature"/> an id if it has none, and returns it. Idempotent: an already
    /// identified creature keeps its id, so a creature seen twice (a party member re-entering a supplier path)
    /// is never re-identified.</summary>
    public Creature Assign(Creature creature)
    {
        if (creature.Id == 0)
            creature.Id = Next();
        return creature;
    }
}
