using System.Collections.Concurrent;

namespace creaturegame.Items;

/// <summary>
/// A transient item inventory: item-id → quantity. Used to gate and consume items during a battle.
/// <para><b>Not persisted yet</b> — there is no save layer (deferred with the Catch milestone). For now
/// the web/session layer seeds a bag per run; later the acquisition layer fills it. The battle engine
/// consumes from it when an item is successfully used.</para>
/// <para>Backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>: the battle loop is the single writer
/// (consume on use), but a web request thread may read <see cref="Entries"/> concurrently for the bag UI,
/// and a concurrent dictionary enumeration is safe (a plain <c>Dictionary</c> can throw on a racing read).</para>
/// </summary>
public sealed class Bag
{
    /// <summary>The Gen 1 per-slot stack ceiling — a bag slot never exceeds 99 of one item. Reward drops add
    /// one at a time so never approach it; the shop's repeatable buy is the first path that can, so
    /// <see cref="Add"/> clamps here and callers gate a purchase on <see cref="IsFull"/>.</summary>
    public const int MaxPerSlot = 99;

    private readonly ConcurrentDictionary<int, int> _quantities = new();

    public Bag() { }

    public Bag(IEnumerable<KeyValuePair<int, int>> entries)
    {
        foreach (var (id, qty) in entries)
            if (qty > 0)
                _quantities[id] = qty;
    }

    /// <summary>Builds a bag holding <paramref name="quantity"/> of each of the given item ids — used to
    /// seed a run's starting bag (e.g. the generous test loadout from the item catalog).</summary>
    public static Bag WithEach(IEnumerable<int> itemIds, int quantity)
    {
        var bag = new Bag();
        foreach (var id in itemIds)
            bag.Add(id, quantity);
        return bag;
    }

    /// <summary>How many of <paramref name="itemId"/> are held (0 if none).</summary>
    public int Count(int itemId) => _quantities.TryGetValue(itemId, out var q) ? q : 0;

    public bool Has(int itemId) => Count(itemId) > 0;

    /// <summary>True when this slot is at the <see cref="MaxPerSlot"/> ceiling — a further add is a no-op, so
    /// the shop refuses a buy that would exceed it (rather than charging for a clamped nothing).</summary>
    public bool IsFull(int itemId) => Count(itemId) >= MaxPerSlot;

    /// <summary>Adds <paramref name="quantity"/> of <paramref name="itemId"/>, clamped at the Gen 1
    /// <see cref="MaxPerSlot"/> ceiling (a slot never exceeds 99). Single-writer (the run/battle loop), so the
    /// read-modify-write needs no extra locking.</summary>
    public void Add(int itemId, int quantity = 1)
    {
        if (quantity <= 0)
            return;
        _quantities[itemId] = Math.Min(Count(itemId) + quantity, MaxPerSlot);
    }

    /// <summary>Removes one of <paramref name="itemId"/>; returns false (no change) if none are held.
    /// Called only from the single-writer battle loop, so the read-modify-write needs no extra locking.</summary>
    public bool Consume(int itemId)
    {
        int held = Count(itemId);
        if (held <= 0)
            return false;
        if (held == 1)
            _quantities.TryRemove(itemId, out _);
        else
            _quantities[itemId] = held - 1;
        return true;
    }

    /// <summary>Held (itemId, quantity) pairs, for surfacing the bag contents.</summary>
    public IReadOnlyDictionary<int, int> Entries => _quantities;
}
