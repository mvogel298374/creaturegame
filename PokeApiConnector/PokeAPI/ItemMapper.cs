using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace PokeApiConnector.PokeAPI;

/// <summary>Turns a PokeAPI <c>/item</c> detail into our <see cref="Item"/>, and owns the Gen 1 item
/// roster (DATA_IMPORT.md §4.5).</summary>
public static class ItemMapper
{
    /// <summary>The Gen 1 battle-usable items, as PokeAPI item slugs (DATA_IMPORT.md §4.5).</summary>
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
        // Revival — Gen 1 shipped Revive only; Max Revive is Gen 2, deliberately excluded (DATA_IMPORT.md §4.5)
        "revive",
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

    /// <summary>Gen 1 gameplay numbers PokeAPI can't express, by item name (DATA_IMPORT.md §4.5).</summary>
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
            case "full-restore":
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
            case "full-heal":
                item.CuresAllStatus = true;
                break;

            case "revive":
                item.RevivePercent = 50;
                break;

            // PP restore — Ether/Max Ether target one move; Elixir/Max Elixir restore every move.
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
            case "dire-hit":
                item.BoostsCrit = true;
                break;
            case "guard-spec":
                item.SetsMist = true;
                break;
        }
    }
}
