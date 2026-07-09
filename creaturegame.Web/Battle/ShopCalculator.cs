using creaturegame.Combat;
using creaturegame.Items;

namespace creaturegame.Web.Battle;

/// <summary>
/// The shop policy behind the run's injected shop supplier (<c>RunDirector</c>'s
/// <c>Func&lt;ShopStockContext, IRandomSource, ShopOffer&gt;</c>) — which items stock a visit and their
/// run-scaled prices. The spend-side sibling of <see cref="RewardCalculator"/>: run-layer roguelite tuning, not
/// a battle seam, and <c>internal static</c> + pure so it's unit-testable the same way. Reuses the reward
/// policy's rarity machinery (<see cref="RewardCalculator.UsableItems"/> / <c>RollRarity</c> / <c>RarityOf</c>)
/// so loot and shop stock speak the same rarity language.
///
/// <para><b>Pricing is rarity-derived, not the Gen 1 shop cost.</b> Raw <c>Item.Cost</c> is 100s–1000s while a
/// run's whole wallet is ~tens of ₽ (the reward gold curve), so a Gen-1-priced shop would sell nothing. Instead
/// each item is priced off its <see cref="RewardRarity"/> band (Common cheap sustain … Epic premium restores),
/// lifted a little by run depth as gold inflates. All magic numbers are provisional balance tuning — the tests
/// assert only the <em>shape</em> (price climbs with rarity, stock is distinct items drawn from the usable
/// pool).</para>
/// </summary>
internal static class ShopCalculator
{
    /// <summary>How many distinct items a visit stocks (capped by the usable pool size).</summary>
    private const int StockSize = 4;

    /// <summary>The cheapest an item can be priced — the Common base band (before any depth lift / jitter). The
    /// run uses it as the <c>RunDirector</c>'s shop-affordability floor: a biome only keeps a Shop node if the
    /// wallet clears this at biome entry, so a broke player never gets a dead, all-unaffordable shop.</summary>
    public const int MinItemPrice = 8;

    /// <summary>The single entry point for the run's shop supplier: rolls this visit's stock — up to
    /// <see cref="StockSize"/> distinct items, each rarity-rolled (depth-biased) and priced off its band. Empty
    /// pool → <see cref="ShopOffer.None"/> (the node resolves as a silent banner).</summary>
    public static ShopOffer BuildStock(int depth, IReadOnlyList<Item> usable, IRandomSource rng)
    {
        if (usable.Count == 0)
            return ShopOffer.None;

        var items = new List<ShopOfferItem>();
        var chosenIds = new HashSet<int>();
        int slots = Math.Min(StockSize, usable.Count);
        for (int i = 0; i < slots; i++)
        {
            var pick = PickDistinct(usable, chosenIds, depth, rng);
            if (pick is null)
                break;
            chosenIds.Add(pick.Id);
            var rarity = RewardCalculator.RarityOf(pick);
            items.Add(
                new ShopOfferItem(pick.Id, pick.Name ?? "", PriceFor(rarity, depth, rng), rarity)
            );
        }

        return items.Count == 0 ? ShopOffer.None : new ShopOffer(items);
    }

    // Draws one item not already stocked: roll a rarity (depth-biased, reusing the reward table) and pick from
    // that band, widening to the whole remaining pool if the band is empty — so a slot is always filled while
    // items remain. Shop uses the default (Common-heavy) rarity weights, so a visit leans cheap sustain with the
    // occasional premium item. Returns null only when nothing distinct is left.
    private static Item? PickDistinct(
        IReadOnlyList<Item> usable,
        HashSet<int> exclude,
        int depth,
        IRandomSource rng
    )
    {
        var pool = usable.Where(i => !exclude.Contains(i.Id)).ToList();
        if (pool.Count == 0)
            return null;

        var rarity = RewardCalculator.RollRarity(RunNodeKind.Shop, depth, rng);
        var band = pool.Where(i => RewardCalculator.RarityOf(i) == rarity).ToList();
        if (band.Count == 0)
            band = pool;
        return band[rng.Next(band.Count)];
    }

    // Base price per rarity band (₽), tuned to the run's gold curve (gold bags are ~tens). Well-separated so the
    // bands never overlap after the depth lift + jitter — price is monotone in rarity by construction.
    private static int BasePrice(RewardRarity rarity) =>
        rarity switch
        {
            RewardRarity.Common => MinItemPrice,
            RewardRarity.Uncommon => 20,
            RewardRarity.Rare => 55,
            RewardRarity.Epic => 90,
            _ => MinItemPrice,
        };

    /// <summary>The shelf price: the rarity's base band, lifted by run depth (deeper shops cost more, as the
    /// reward gold curve inflates) and nudged by small ±10% jitter so two same-rarity items aren't identically
    /// priced. Provisional tuning. <c>internal</c> so the rarity-ordering shape is directly unit-testable.</summary>
    internal static int PriceFor(RewardRarity rarity, int depth, IRandomSource rng)
    {
        int baseline = BasePrice(rarity);
        double lift = 1.0 + Math.Min(Math.Max(0, depth) * 0.04, 0.8); // up to +80% deep in a run
        double jitter = 0.9 + rng.NextDouble() * 0.2; // ±10%
        return Math.Max(1, (int)Math.Round(baseline * lift * jitter));
    }
}
