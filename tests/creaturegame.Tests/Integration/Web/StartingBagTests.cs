using creaturegame.Items;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The run's starting bag (<see cref="EncounterFactory.BuildStartingBag"/>) — a curated, modest loadout by
/// category/cost (no hardcoded item ids), replacing the old "every item ×20" test seed now that the run
/// economy grows the bag through play. Pins that it stocks the cheapest of each useful category in small
/// quantities and never seeds dead-loot (Ball/Revive) — a lucky early start can't trivialise a run.
/// </summary>
public class StartingBagTests
{
    private static Item MakeItem(int id, ItemCategory category, int cost) =>
        new()
        {
            Id = id,
            Name = $"item{id}",
            Category = category,
            Cost = cost,
        };

    private static IReadOnlyList<Item> Catalog() =>
        [
            MakeItem(10, ItemCategory.Healing, 300), // cheapest Healing
            MakeItem(11, ItemCategory.Healing, 700),
            MakeItem(12, ItemCategory.Healing, 1200),
            MakeItem(20, ItemCategory.StatusCure, 100), // cheapest StatusCure
            MakeItem(21, ItemCategory.StatusCure, 150), // 2nd cheapest
            MakeItem(22, ItemCategory.StatusCure, 250),
            MakeItem(30, ItemCategory.PpRestore, 1200), // cheapest PpRestore
            MakeItem(31, ItemCategory.PpRestore, 3000),
            MakeItem(40, ItemCategory.BattleStatBoost, 500),
            MakeItem(50, ItemCategory.Ball, 200),
            MakeItem(51, ItemCategory.Revive, 1500),
        ];

    [Fact]
    public void BuildStartingBag_StocksCheapestHealingXFour()
    {
        var bag = EncounterFactory.BuildStartingBag(Catalog());

        Assert.Equal(4, bag.Count(10)); // cheapest Healing ×4
        Assert.Equal(0, bag.Count(11)); // pricier Healing items are not seeded
        Assert.Equal(0, bag.Count(12));
    }

    [Fact]
    public void BuildStartingBag_StocksTwoCheapestStatusCures_OneEach()
    {
        var bag = EncounterFactory.BuildStartingBag(Catalog());

        Assert.Equal(1, bag.Count(20)); // two cheapest StatusCures, ×1
        Assert.Equal(1, bag.Count(21));
        Assert.Equal(0, bag.Count(22)); // the third is not seeded
    }

    [Fact]
    public void BuildStartingBag_StocksCheapestPpRestore_One()
    {
        var bag = EncounterFactory.BuildStartingBag(Catalog());

        Assert.Equal(1, bag.Count(30));
        Assert.Equal(0, bag.Count(31));
    }

    [Fact]
    public void BuildStartingBag_NeverSeedsBallReviveOrStatBoost()
    {
        var bag = EncounterFactory.BuildStartingBag(Catalog());

        Assert.Equal(0, bag.Count(40)); // BattleStatBoost isn't part of the modest start
        Assert.Equal(0, bag.Count(50)); // Ball — dead loot, never seeded
        Assert.Equal(0, bag.Count(51)); // Revive — dead loot, never seeded
    }

    [Fact]
    public void BuildStartingBag_IsModest_NotTheWholeCatalog()
    {
        var bag = EncounterFactory.BuildStartingBag(Catalog());

        // 4 (Healing) + 1 + 1 (StatusCure) + 1 (PpRestore) = 7 items across 4 stacks — deliberately light.
        Assert.Equal(7, bag.Entries.Values.Sum());
        Assert.Equal(4, bag.Entries.Count);
    }

    [Fact]
    public void BuildStartingBag_EmptyCatalog_YieldsEmptyBag()
    {
        var bag = EncounterFactory.BuildStartingBag([]);

        Assert.Empty(bag.Entries);
    }
}
