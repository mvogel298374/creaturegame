namespace creaturegame.Items;

/// <summary>
/// A transient item inventory: item-id → quantity. Used to gate and consume items during a battle.
/// <para><b>Not persisted yet</b> — there is no save layer (deferred with the Catch milestone). For now
/// the web/session layer seeds a bag per run; later the acquisition layer fills it. The battle engine
/// consumes from it when an item is successfully used.</para>
/// </summary>
public sealed class Bag
{
    private readonly Dictionary<int, int> _quantities = new();

    public Bag() { }

    public Bag(IEnumerable<KeyValuePair<int, int>> entries)
    {
        foreach (var (id, qty) in entries)
            if (qty > 0)
                _quantities[id] = qty;
    }

    /// <summary>How many of <paramref name="itemId"/> are held (0 if none).</summary>
    public int Count(int itemId) => _quantities.TryGetValue(itemId, out var q) ? q : 0;

    public bool Has(int itemId) => Count(itemId) > 0;

    public void Add(int itemId, int quantity = 1)
    {
        if (quantity <= 0)
            return;
        _quantities[itemId] = Count(itemId) + quantity;
    }

    /// <summary>Removes one of <paramref name="itemId"/>; returns false (no change) if none are held.</summary>
    public bool Consume(int itemId)
    {
        int held = Count(itemId);
        if (held <= 0)
            return false;
        if (held == 1)
            _quantities.Remove(itemId);
        else
            _quantities[itemId] = held - 1;
        return true;
    }

    /// <summary>Held (itemId, quantity) pairs, for surfacing the bag contents.</summary>
    public IReadOnlyDictionary<int, int> Entries => _quantities;
}
