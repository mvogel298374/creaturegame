namespace creaturegame.Items;

/// <summary>
/// A transient per-run currency, mirroring <see cref="Bag"/>'s shape and threading.
/// <para><b>Not persisted yet</b> — there is no save layer; the web/session layer holds one per run and it is
/// lost on death, same as <see cref="Bag"/>. The run loop is the single writer (credits on reward), while a web
/// request thread may read <see cref="Balance"/> concurrently for the gold HUD.</para>
/// </summary>
public sealed class Wallet
{
    private volatile int _balance;

    public int Balance => _balance;

    /// <summary>Called only from the single-writer run loop, so the read-modify-write needs no extra
    /// locking (mirrors <see cref="Bag.Add"/>); <c>volatile</c> keeps a concurrent HUD read fresh.</summary>
    public void Credit(int amount)
    {
        if (amount <= 0)
            return;
        _balance += amount;
    }

    /// <summary>Spends <paramref name="amount"/> if affordable; returns false (no change) otherwise.
    /// Not consumed by anything yet — the future Shop's spend path (mirrors <see cref="Bag.Consume"/>).</summary>
    public bool TrySpend(int amount)
    {
        if (amount <= 0 || _balance < amount)
            return false;
        _balance -= amount;
        return true;
    }
}
