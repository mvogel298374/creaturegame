using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace PokeApiConnector.PokeAPI;

/// <summary>
/// Turns a PokeAPI <c>/item</c> detail into our <see cref="Item"/>, and owns the Gen 1 item roster.
/// <para>Unlike moves/species, PokeAPI gives NO reliable Gen 1 membership signal for items — an item's
/// <c>game_indices</c> and <c>flavor_text_entries</c> both only reach back to Gen 3 (Poké Ball has no
/// Gen 1 entry). So — exactly as <c>GameAvailabilitySeeder</c> curates species obtainability the API
/// can't express (DATA_IMPORT.md §4.3/§5.4) — the Gen 1 battle-item roster is encoded here as
/// hand-verified domain knowledge in <see cref="Gen1BattleItemNames"/>, which also drives what the
/// importer fetches.</para>
/// <para>The well-defined Gen 1 gameplay numbers PokeAPI can't express (heal amount, cured status, …)
/// are filled in <see cref="ApplyGen1Gameplay"/> from an authority — the move importer's layer-2
/// strategy (DATA_IMPORT.md §4.1/§5.5). Poké Ball catch-rate multipliers are deliberately NOT modelled
/// here: Gen 1 capture is a battle formula that belongs with the (deferred) Catch mechanic.</para>
/// </summary>
public static class ItemMapper
{
    /// <summary>
    /// The Gen 1 battle-usable items, as PokeAPI item slugs. Hand-curated because the API has no Gen 1
    /// membership signal for items. Excludes evolution stones, vitamins, Rare Candy, key items, TMs and
    /// berries — see TODO "Item System — Data Import". ("x-sp-atk" is the modern slug for Gen 1's X
    /// Special; X Sp. Def did not exist in Gen 1.)
    /// </summary>
    public static readonly IReadOnlySet<string> Gen1BattleItemNames = new HashSet<string>
    {
        // Poké Balls
        "poke-ball",
        "great-ball",
        "ultra-ball",
        "master-ball",
        "safari-ball",
        // Healing
        "potion",
        "super-potion",
        "hyper-potion",
        "max-potion",
        "full-restore",
        // Status cures
        "antidote",
        "burn-heal",
        "ice-heal",
        "awakening",
        "paralyze-heal",
        "full-heal",
        // Revival
        "revive",
        "max-revive",
        // PP restore
        "ether",
        "max-ether",
        "elixir",
        "max-elixir",
        // X-items
        "x-attack",
        "x-defense",
        "x-speed",
        "x-sp-atk",
        "x-accuracy",
        "dire-hit",
        "guard-spec",
    };

    public static Item MapToItem(PokeApiItem pokeItem)
    {
        Item item = new Item
        {
            Id = pokeItem.Id,
            Name = pokeItem.Name,
            Category = MapCategory(pokeItem.Category?.Name),
            Cost = pokeItem.Cost,
            FlingPower = pokeItem.FlingPower,
            Description =
                pokeItem.EffectEntries?.FirstOrDefault(e => e.Language?.Name == "en")?.ShortEffect
                ?? "No description available.",
            SpriteUrl = pokeItem.Sprites?.Default,
        };

        ApplyGen1Gameplay(item);
        return item;
    }

    // Our coarse grouping from PokeAPI's finer item-category names.
    private static ItemCategory MapCategory(string? pokeApiCategory) =>
        pokeApiCategory switch
        {
            "standard-balls" or "special-balls" => ItemCategory.Ball,
            "healing" => ItemCategory.Healing,
            "status-cures" => ItemCategory.StatusCure,
            "revival" => ItemCategory.Revive,
            "pp-recovery" => ItemCategory.PpRestore,
            "stat-boosts" => ItemCategory.BattleStatBoost,
            _ => ItemCategory.Other,
        };

    // ── Gen 1 gameplay numbers (layer 2: facts PokeAPI's structured data can't express) ───────────
    // PokeAPI gives no machine-readable "this heals 20 HP" / "this cures poison", so the well-defined
    // Gen 1 numbers are set here by item name from an authority (Bulbapedia). Keep it verified and
    // commented — the same layered strategy the move importer uses.
    private static void ApplyGen1Gameplay(Item item)
    {
        switch (item.Name)
        {
            // Healing — fixed HP
            case "potion":
                item.HealAmount = 20;
                break;
            case "super-potion":
                item.HealAmount = 50;
                break;
            case "hyper-potion":
                item.HealAmount = 200;
                break;
            case "max-potion":
                item.HealsAllHp = true;
                break;
            case "full-restore": // restores all HP and cures all status
                item.HealsAllHp = true;
                item.CuresAllStatus = true;
                break;

            // Status cures — single status
            case "antidote":
                item.CuredStatus = StatusCondition.Poison;
                break;
            case "burn-heal":
                item.CuredStatus = StatusCondition.Burn;
                break;
            case "ice-heal":
                item.CuredStatus = StatusCondition.Freeze;
                break;
            case "awakening":
                item.CuredStatus = StatusCondition.Sleep;
                break;
            case "paralyze-heal":
                item.CuredStatus = StatusCondition.Paralysis;
                break;
            case "full-heal": // cures all status
                item.CuresAllStatus = true;
                break;

            // Revival
            case "revive":
                item.RevivePercent = 50;
                break;
            case "max-revive":
                item.RevivePercent = 100;
                break;

            // PP restore. Ether/Elixir restore 10 PP; Max variants fully restore. Ether/Max Ether target
            // ONE move; Elixir/Max Elixir restore EVERY move (RestoresPpAllMoves).
            case "ether":
                item.PpRestoreAmount = 10;
                break;
            case "max-ether":
                item.RestoresAllPp = true;
                break;
            case "elixir":
                item.PpRestoreAmount = 10;
                item.RestoresPpAllMoves = true;
                break;
            case "max-elixir":
                item.RestoresAllPp = true;
                item.RestoresPpAllMoves = true;
                break;

            // X-items — raise one stat by one stage in battle ("x-special" is current "x-sp-atk").
            case "x-attack":
                item.StatBoostStat = StageStat.Attack;
                item.StatBoostStages = 1;
                break;
            case "x-defense":
                item.StatBoostStat = StageStat.Defense;
                item.StatBoostStages = 1;
                break;
            case "x-speed":
                item.StatBoostStat = StageStat.Speed;
                item.StatBoostStages = 1;
                break;
            case "x-special":
            case "x-sp-atk":
                item.StatBoostStat = StageStat.Special;
                item.StatBoostStages = 1;
                break;
            case "x-accuracy":
                item.StatBoostStat = StageStat.Accuracy;
                item.StatBoostStages = 1;
                break;
            // dire-hit (boosts crit) and guard-spec (sets Mist) are battle-usable but their effects
            // aren't a stat-stage change — left as data-only rows; their effect is deferred to the
            // use-in-battle layer.
        }
    }
}
