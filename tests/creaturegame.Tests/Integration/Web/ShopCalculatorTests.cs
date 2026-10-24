using creaturegame.Combat;
using creaturegame.Items;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The run economy's shop policy (<see cref="ShopCalculator"/>) — the per-visit stock + run-scaled pricing
/// behind <see cref="RunDirector"/>'s injected shop supplier. The spend-side sibling of
/// <see cref="RewardCalculatorTests"/>: pins the <em>shape</em> (stock is distinct sellable items drawn from the
/// usable pool, price follows the Gen 1 order, an empty pool yields no stock, seeded reproducibility) without
/// over-fitting the provisional divisor.
/// </summary>
public class ShopCalculatorTests
{
    private static Item MakeItem(int id, ItemCategory category, int cost, string? name = null) =>
        new()
        {
            Id = id,
            Name = name ?? $"item{id}",
            Category = category,
            Cost = cost,
        };

    // A usable catalog spanning all four rarity bands (by Gen 1 cost) so stock can draw any rarity.
    private static IReadOnlyList<Item> UsableCatalog() =>
        RewardCalculator.UsableItems([
            MakeItem(1, ItemCategory.Healing, 200), // Common
            MakeItem(2, ItemCategory.PpRestore, 900), // Uncommon
            MakeItem(3, ItemCategory.Healing, 1500), // Rare
            MakeItem(4, ItemCategory.Healing, 3000), // Epic
            MakeItem(5, ItemCategory.StatusCure, 250), // Common
            MakeItem(6, ItemCategory.BattleStatBoost, 1000), // Uncommon
        ]);

    [Fact]
    public void BuildStock_DrawsDistinctItemsFromTheUsablePool()
    {
        var usable = UsableCatalog();
        var usableIds = usable.Select(i => i.Id).ToHashSet();
        var rng = new SeededRandomSource(7);

        for (int i = 0; i < 2000; i++)
        {
            var offer = ShopCalculator.BuildStock(depth: 3, usable, rng);

            Assert.NotEmpty(offer.Items);
            // Every stock item is a real usable item, and the stock holds no duplicates.
            Assert.All(offer.Items, it => Assert.Contains(it.ItemId, usableIds));
            Assert.Equal(offer.Items.Count, offer.Items.Select(it => it.ItemId).Distinct().Count());
            // Every priced item is affordable-shaped: a positive ₽ price.
            Assert.All(offer.Items, it => Assert.True(it.Price > 0));
        }
    }

    [Fact]
    public void BuildStock_CapsStockAtTheUsablePoolSize()
    {
        // A pool smaller than the stock size narrows gracefully to the whole pool (never duplicates to pad out).
        IReadOnlyList<Item> pool =
        [
            MakeItem(1, ItemCategory.Healing, 200),
            MakeItem(2, ItemCategory.Healing, 1500),
        ];
        var offer = ShopCalculator.BuildStock(depth: 0, pool, new SeededRandomSource(3));

        Assert.Equal(2, offer.Items.Count);
        Assert.Equal(new[] { 1, 2 }, offer.Items.Select(i => i.ItemId).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void BuildStock_CanStockRevive()
    {
        // Unlike the reward roll (Boss-only), the shop draws Revive straight from the usable pool — the rare
        // premium-stock acquisition channel. Over many visits of a small pool holding one, it surfaces.
        var usable = RewardCalculator.UsableItems([
            MakeItem(1, ItemCategory.Healing, 200),
            MakeItem(2, ItemCategory.Revive, 1500),
        ]);
        var rng = new SeededRandomSource(19);
        bool sawRevive = false;
        for (int i = 0; i < 500 && !sawRevive; i++)
            sawRevive = ShopCalculator
                .BuildStock(depth: 3, usable, rng)
                .Items.Any(it => it.ItemId == 2);
        Assert.True(sawRevive, "the shop should be able to stock a Revive");
    }

    [Fact]
    public void BuildStock_EmptyPool_YieldsNoStock()
    {
        var offer = ShopCalculator.BuildStock(depth: 5, [], new SeededRandomSource(1));
        Assert.True(offer.IsEmpty);
        Assert.Same(ShopOffer.None, offer);
    }

    [Fact]
    public void PriceFor_FollowsTheGen1PriceOrder_AtTheSameDepth()
    {
        // Same jitter draw for each, isolating the Gen 1 cost.
        int[] gen1Prices = [100, 300, 600, 700, 950, 1500, 2500, 3000];
        var prices = gen1Prices
            .Select(cost =>
                ShopCalculator.PriceFor(
                    MakeItem(1, ItemCategory.Healing, cost),
                    depth: 0,
                    new SeededRandomSource(5)
                )
            )
            .ToArray();

        for (int i = 1; i < prices.Length; i++)
            Assert.True(
                prices[i - 1] < prices[i],
                $"Gen 1 {gen1Prices[i - 1]} → {prices[i - 1]} should undercut {gen1Prices[i]} → {prices[i]}"
            );
    }

    [Fact]
    public void PriceFor_LiftsWithDepth_AtTheSameItem()
    {
        // Deeper shops cost more (gold inflates over a run) — same seed so only the depth lift differs.
        var hyperPotion = MakeItem(1, ItemCategory.Healing, 1500);
        int shallow = ShopCalculator.PriceFor(hyperPotion, depth: 0, new SeededRandomSource(9));
        int deep = ShopCalculator.PriceFor(hyperPotion, depth: 20, new SeededRandomSource(9));

        Assert.True(deep > shallow, $"deep {deep} should out-price shallow {shallow}");
    }

    [Fact]
    public void BuildStock_PricesEachItemOffItsOwnGen1Cost()
    {
        var usable = UsableCatalog();
        var byId = usable.ToDictionary(i => i.Id);
        var rng = new SeededRandomSource(42);

        for (int i = 0; i < 500; i++)
        {
            var offer = ShopCalculator.BuildStock(depth: 2, usable, rng);
            foreach (var stocked in offer.Items)
            {
                var item = byId[stocked.ItemId];
                int lo = ShopCalculator.PriceFor(item, 2, new AlwaysLowJitter());
                int hi = ShopCalculator.PriceFor(item, 2, new AlwaysHighJitter());
                Assert.InRange(stocked.Price, lo, hi);
                Assert.Equal(RewardCalculator.RarityOf(item), stocked.Rarity);
            }
        }
    }

    [Fact]
    public void BuildStock_NeverStocksALootOnlyItem()
    {
        // Gen 1 never sold PP items (price 0): the run offers them as loot, never on a shelf.
        var usable = RewardCalculator.UsableItems([
            MakeItem(1, ItemCategory.Healing, 300),
            MakeItem(38, ItemCategory.PpRestore, 0),
            MakeItem(41, ItemCategory.PpRestore, 0),
        ]);
        var rng = new SeededRandomSource(11);

        for (int i = 0; i < 500; i++)
            Assert.All(
                ShopCalculator.BuildStock(depth: 3, usable, rng).Items,
                it => Assert.Equal(1, it.ItemId)
            );
    }

    [Fact]
    public void BuildStock_OnlyLootOnlyItems_YieldsNoStock()
    {
        var usable = RewardCalculator.UsableItems([MakeItem(38, ItemCategory.PpRestore, 0)]);

        Assert.Same(
            ShopOffer.None,
            ShopCalculator.BuildStock(3, usable, new SeededRandomSource(1))
        );
    }

    [Fact]
    public void AffordabilityFloor_IsTheCheapestHealsLowestPrice_NotACheaperCure()
    {
        // Antidote (¥100) undercuts Potion (¥300), but the floor is the cheapest heal's price.
        var potion = MakeItem(17, ItemCategory.Healing, 300);
        IReadOnlyList<Item> usable =
        [
            MakeItem(18, ItemCategory.StatusCure, 100),
            potion,
            MakeItem(25, ItemCategory.Healing, 1500),
        ];

        int floor = ShopCalculator.AffordabilityFloor(usable);

        Assert.Equal(ShopCalculator.PriceFor(potion, 0, new AlwaysLowJitter()), floor);
        var rng = new SeededRandomSource(77);
        for (int i = 0; i < 2000; i++)
        {
            int potionPrice = ShopCalculator.PriceFor(potion, depth: i % 30, rng);
            Assert.True(potionPrice >= floor, $"Potion at {potionPrice} under the floor {floor}");
        }
    }

    [Fact]
    public void AffordabilityFloor_FallsBackToTheCheapestSellable_WithNoHeal()
    {
        var antidote = MakeItem(18, ItemCategory.StatusCure, 100);
        IReadOnlyList<Item> usable = [antidote, MakeItem(57, ItemCategory.BattleStatBoost, 500)];

        Assert.Equal(
            ShopCalculator.PriceFor(antidote, 0, new AlwaysLowJitter()),
            ShopCalculator.AffordabilityFloor(usable)
        );
    }

    [Fact]
    public void AffordabilityFloor_IgnoresLootOnlyItems_AndIsZeroWithNothingToSell()
    {
        Item[] lootOnly = [MakeItem(38, ItemCategory.PpRestore, 0)];

        Assert.Equal(0, ShopCalculator.AffordabilityFloor(lootOnly));
        Assert.Equal(
            ShopCalculator.AffordabilityFloor([MakeItem(1, ItemCategory.Healing, 3000)]),
            ShopCalculator.AffordabilityFloor([
                .. lootOnly,
                MakeItem(1, ItemCategory.Healing, 3000),
            ])
        );
    }

    [Fact]
    public void BuildStock_IsReproducibleFromSeed()
    {
        var usable = UsableCatalog();

        var a = ShopCalculator.BuildStock(3, usable, new SeededRandomSource(123));
        var b = ShopCalculator.BuildStock(3, usable, new SeededRandomSource(123));

        Assert.Equal(Describe(a), Describe(b));
    }

    // A stable string signature of an offer, for seeded-reproducibility comparison.
    private static string Describe(ShopOffer offer) =>
        string.Join("|", offer.Items.Select(i => $"{i.ItemId}:{i.Rarity}:{i.Price}"));

    // Jitter endpoints: PriceFor's jitter is 0.9 + NextDouble()*0.2, so NextDouble()=0 is the low end and →1 is
    // the high end. These pin the price window a given (item, depth) can produce.
    private sealed class AlwaysLowJitter : IRandomSource
    {
        public int Next(int max) => 0;

        public int Next(int min, int max) => min;

        public double NextDouble() => 0.0;
    }

    private sealed class AlwaysHighJitter : IRandomSource
    {
        public int Next(int max) => 0;

        public int Next(int min, int max) => min;

        public double NextDouble() => 1.0; // real draws are < 1.0, so this bounds every possible price from above
    }
}
