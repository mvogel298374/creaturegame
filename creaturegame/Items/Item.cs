using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Items;

/// <summary>
/// A Gen 1 battle-usable item (Poké Balls, healing, status cures, revives, PP restore, X-items).
/// Imported from PokeAPI by <c>ItemImport</c> into <c>items.db</c>; the runtime reads it via
/// <c>ItemService</c>. This is data only — the bag / use-in-battle layer is not built yet.
/// </summary>
public class Item
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public ItemCategory Category { get; set; } = ItemCategory.Other;

    /// <summary>Mart buy price in Poké-dollars (PokeAPI <c>cost</c>).</summary>
    public int Cost { get; set; }

    /// <summary>Base power if thrown with Fling (later gen mechanic; kept as raw imported data).</summary>
    public int? FlingPower { get; set; }

    /// <summary>English short-effect text from PokeAPI.</summary>
    public string? Description { get; set; }

    /// <summary>PokeAPI default sprite URL (sprite download into wwwroot is deferred to UI time).</summary>
    public string? SpriteUrl { get; set; }

    // ── Gen 1 gameplay numbers (layer-2 override block in ItemImport) ─────────────────────────────
    // PokeAPI's structured data doesn't model what an item *does* in Gen 1, so these well-defined
    // Gen 1 facts are filled from an authority at map time (the moves-importer layer-2 pattern).
    // Null/false means "not applicable to this item". Catch-rate multipliers for Poké Balls are
    // deliberately NOT stored here — Gen 1 capture math is a battle-rule/formula concern that belongs
    // with the (deferred) Catch mechanic, not the item data row.

    /// <summary>Fixed HP restored (Potion 20, Super Potion 50, Hyper Potion 200). Null if not a fixed HP heal.</summary>
    public int? HealAmount { get; set; }

    /// <summary>Restores the target's full HP (Max Potion, Full Restore).</summary>
    public bool HealsAllHp { get; set; }

    /// <summary>Cures every major status (Full Heal, Full Restore).</summary>
    public bool CuresAllStatus { get; set; }

    /// <summary>The single status this item cures (Antidote → Poison, Burn Heal → Burn, …). Null if none/all.</summary>
    public StatusCondition? CuredStatus { get; set; }

    /// <summary>Percent of max HP a fainted Pokémon is revived with (Revive 50, Max Revive 100).</summary>
    public int? RevivePercent { get; set; }

    /// <summary>PP restored to one move (Ether 10, Elixir 10). Null when not a fixed PP restore.</summary>
    public int? PpRestoreAmount { get; set; }

    /// <summary>Fully restores PP (Max Ether for one move, Max Elixir for all moves).</summary>
    public bool RestoresAllPp { get; set; }

    /// <summary>X-item stat raised (X Attack → Attack, …) and by how many stages. Null when not an X-item.</summary>
    public StageStat? StatBoostStat { get; set; }
    public int? StatBoostStages { get; set; }

    public Item() { }

    public override string ToString() => $"Name: {Name}, Category: {Category}, Cost: {Cost}";
}
