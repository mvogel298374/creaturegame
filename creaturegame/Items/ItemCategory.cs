namespace creaturegame.Items;

/// <summary>
/// Our coarse grouping of the Gen 1 battle-usable items, mapped from PokeAPI's finer
/// item-category names in <c>ItemImport</c> (e.g. <c>standard-balls</c>/<c>special-balls</c> → Ball).
/// </summary>
public enum ItemCategory
{
    Other,

    /// <summary>Poké / Great / Ultra / Master / Safari Ball.</summary>
    Ball,

    /// <summary>HP restore — Potion line, Max Potion, Full Restore.</summary>
    Healing,

    /// <summary>Status cure — Antidote, Burn Heal, Ice Heal, Awakening, Paralyze Heal, Full Heal.</summary>
    StatusCure,

    /// <summary>Revive, Max Revive.</summary>
    Revive,

    /// <summary>PP restore — Ether, Max Ether, Elixir, Max Elixir.</summary>
    PpRestore,

    /// <summary>In-battle stat booster — X Attack/Defense/Speed/Special/Accuracy, Dire Hit, Guard Spec.</summary>
    BattleStatBoost,
}
