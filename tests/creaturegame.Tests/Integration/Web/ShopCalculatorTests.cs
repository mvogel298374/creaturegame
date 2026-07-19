using creaturegame.Combat;
using creaturegame.Items;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The run economy's shop policy (<see cref="ShopCalculator"/>) — the per-visit stock + run-scaled pricing
/// behind <see cref="RunDirector"/>'s injected shop supplier. The spend-side sibling of
/// <see cref="RewardCalculatorTests"/>: pins the <em>shape</em> (stock is distinct items drawn from the usable
/// pool, price climbs with rarity, an empty pool yields no stock, seeded reproducibility) without over-fitting
/// the provisional price band.
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
    public void PriceFor_ClimbsWithRarity_AtTheSameDepth()
    {
        // The headline shape: rarer costs more. The bands are well-separated, so the ordering holds regardless of
        // the ±10% jitter (same seed for each so the jitter draw is identical, isolating the rarity factor).
        int common = ShopCalculator.PriceFor(
            RewardRarity.Common,
            depth: 0,
            new SeededRandomSource(5)
        );
        int uncommon = ShopCalculator.PriceFor(
            RewardRarity.Uncommon,
            depth: 0,
            new SeededRandomSource(5)
        );
        int rare = ShopCalculator.PriceFor(RewardRarity.Rare, depth: 0, new SeededRandomSource(5));
        int epic = ShopCalculator.PriceFor(RewardRarity.Epic, depth: 0, new SeededRandomSource(5));

        Assert.True(common < uncommon, $"Common {common} < Uncommon {uncommon}");
        Assert.True(uncommon < rare, $"Uncommon {uncommon} < Rare {rare}");
        Assert.True(rare < epic, $"Rare {rare} < Epic {epic}");
    }

    [Fact]
    public void PriceFor_LiftsWithDepth_AtTheSameRarity()
    {
        // Deeper shops cost more (gold inflates over a run) — same seed so only the depth lift differs.
        int shallow = ShopCalculator.PriceFor(
            RewardRarity.Rare,
            depth: 0,
            new SeededRandomSource(9)
        );
        int deep = ShopCalculator.PriceFor(RewardRarity.Rare, depth: 20, new SeededRandomSource(9));

        Assert.True(deep > shallow, $"deep {deep} should out-price shallow {shallow}");
    }

    [Fact]
    public void BuildStock_ItemPriceMatchesItsRarityBand()
    {
        // Each stock item's price must be the price of the rarity it carries (not some other band) — proves the
        // price is derived from the item's own rarity, so the client's rarity colour and the price agree.
        var usable = UsableCatalog();
        var rng = new SeededRandomSource(42);

        for (int i = 0; i < 500; i++)
        {
            var offer = ShopCalculator.BuildStock(depth: 2, usable, rng);
            foreach (var item in offer.Items)
            {
                // The price for this rarity at depth 2 spans a ±10% jitter window around base×lift; assert the
                // item's price sits in that window (so it can't have been priced off a different band).
                int lo = ShopCalculator.PriceFor(item.Rarity, 2, new AlwaysLowJitter());
                int hi = ShopCalculator.PriceFor(item.Rarity, 2, new AlwaysHighJitter());
                Assert.InRange(item.Price, lo, hi);
            }
        }
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
    // the high end. These pin the price window a given (rarity, depth) can produce.
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
