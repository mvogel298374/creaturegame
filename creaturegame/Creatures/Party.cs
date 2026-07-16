namespace creaturegame.Creatures;

/// <summary>
/// The run's party — the up-to-six creatures the player owns this run, one of which is the active
/// <see cref="Lead"/> that battles. Run-level state (like <c>Items.Bag</c> / <c>Combat.Wallet</c>): it lives
/// outside <see cref="Creature"/> and outside the per-battle <c>BattleState</c>, is threaded by the run loop,
/// and is transient today (no save layer — the Catch/save milestone persists it, see <c>STATE_MODEL.md §7</c>).
/// <para>
/// The acquisition channels (themed draft, boss catch — <c>ENCOUNTER_DESIGN.md §4</c>) <see cref="Add"/> to it;
/// the between-biome lead choice <see cref="SetLead"/>s which member is active. An acquired creature keeps its
/// permanent half (level / HP / moves) — the party just owns the collection; a member's transient
/// <c>BattleState</c> is still reset per battle by the engine.
/// </para>
/// </summary>
public sealed class Party
{
    /// <summary>The Gen 1 party ceiling — a trainer carries at most six.</summary>
    public const int MaxSize = 6;

    private readonly List<Creature> _members = [];

    /// <summary>Starts a party with its <paramref name="lead"/> (the run's starter) as the sole, active member.</summary>
    public Party(Creature lead)
    {
        ArgumentNullException.ThrowIfNull(lead);
        _members.Add(lead);
        LeadIndex = 0;
    }

    /// <summary>The index into <see cref="Members"/> of the active member.</summary>
    public int LeadIndex { get; private set; }

    /// <summary>The active creature — the one that battles. Equals <c>RunState.Player</c>.</summary>
    public Creature Lead => _members[LeadIndex];

    /// <summary>All party members in acquisition order (read-only view; the lead is at <see cref="LeadIndex"/>).</summary>
    public IReadOnlyList<Creature> Members => _members;

    /// <summary>How many creatures the party currently holds (1–<see cref="MaxSize"/>).</summary>
    public int Count => _members.Count;

    /// <summary>True when the party is at the <see cref="MaxSize"/> ceiling — a further <see cref="Add"/> is
    /// refused, so an acquisition must instead <see cref="Replace"/> an existing member (or be declined).</summary>
    public bool IsFull => _members.Count >= MaxSize;

    /// <summary>Adds <paramref name="creature"/> to the party; returns false (no change) when already full.</summary>
    public bool Add(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (IsFull)
            return false;
        _members.Add(creature);
        return true;
    }

    /// <summary>Swaps the member at <paramref name="index"/> for <paramref name="creature"/> — the "party full,
    /// release one to make room" acquisition path. No-op if the index is out of range. If the replaced member
    /// was the lead, the new creature becomes the lead (it takes the same slot).</summary>
    public void Replace(int index, Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (index >= 0 && index < _members.Count)
            _members[index] = creature;
    }

    /// <summary>Makes the member at <paramref name="index"/> the active <see cref="Lead"/> — the between-biome
    /// lead choice. No-op if the index is out of range, so a stale pick never strands the run.</summary>
    public void SetLead(int index)
    {
        if (index >= 0 && index < _members.Count)
            LeadIndex = index;
    }
}
