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
/// <para>Pricing → <c>ENCOUNTER_DESIGN.md §5.1</c>.</para>
/// </summary>
internal static class ShopCalculator
{
    /// <summary>How many distinct items a visit stocks (capped by the usable pool size).</summary>
    private const int StockSize = 4;

    /// <summary>Item ₽ per run ₽ (<c>ENCOUNTER_DESIGN.md §5.1</c>).</summary>
    private const double PriceDivisor = 35.0;

    private const double MinJitter = 0.9;
    private const double JitterSpan = 0.2;

    /// <summary>Rolls this visit's stock of distinct sellable items; none → <see cref="ShopOffer.None"/>.</summary>
    public static ShopOffer BuildStock(int depth, IReadOnlyList<Item> usable, IRandomSource rng)
    {
        var sellable = Sellable(usable);
        if (sellable.Count == 0)
            return ShopOffer.None;

        var items = new List<ShopOfferItem>();
        var chosenIds = new HashSet<int>();
        int slots = Math.Min(StockSize, sellable.Count);
        for (int i = 0; i < slots; i++)
        {
            var pick = PickDistinct(sellable, chosenIds, depth, rng);
            if (pick is null)
                break;
            chosenIds.Add(pick.Id);
            items.Add(
                new ShopOfferItem(
                    pick.Id,
                    pick.Name ?? "",
                    PriceFor(pick, depth, rng),
                    RewardCalculator.RarityOf(pick)
                )
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

    private static List<Item> Sellable(IReadOnlyList<Item> usable) =>
        usable.Where(i => !RewardCalculator.IsLootOnly(i)).ToList();

    /// <summary>Shop-affordability floor → <c>ENCOUNTER_DESIGN.md §5.1</c>.</summary>
    public static int AffordabilityFloor(IReadOnlyList<Item> usable)
    {
        var sellable = Sellable(usable);
        var heals = sellable.Where(i => i.Category == ItemCategory.Healing).ToList();
        var basis = heals.Count > 0 ? heals : sellable;
        return basis.Count == 0 ? 0 : basis.Min(i => Price(i.Cost, depth: 0, jitter: MinJitter));
    }

    /// <summary>The shelf price → <c>ENCOUNTER_DESIGN.md §5.1</c>; <c>internal</c> for unit testing.</summary>
    internal static int PriceFor(Item item, int depth, IRandomSource rng) =>
        Price(item.Cost, depth, jitter: MinJitter + rng.NextDouble() * JitterSpan);

    private static int Price(int cost, int depth, double jitter)
    {
        double lift = 1.0 + Math.Min(Math.Max(0, depth) * 0.04, 0.8); // up to +80% deep in a run
        return Math.Max(1, (int)Math.Round(cost / PriceDivisor * lift * jitter));
    }
}
