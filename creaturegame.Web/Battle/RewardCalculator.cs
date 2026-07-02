using creaturegame.Combat;
using creaturegame.Items;

namespace creaturegame.Web.Battle;

/// <summary>
/// The reward policy behind the run's injected reward supplier (<c>RunDirector</c>'s
/// <c>Func&lt;RewardContext, IRandomSource, RunReward&gt;</c>) — drop rates, the gold curve, and item
/// eligibility. Run-layer roguelite tuning, not a battle seam (same class as
/// <see cref="EncounterFactory.ScaleWildLevel"/>): pure and <c>internal static</c> so it's unit-testable
/// exactly like that knob. <see cref="EncounterFactory"/> closes over the usable-item subset and dispatches
/// here by <c>RewardContext.Source</c>.
/// </summary>
internal static class RewardCalculator
{
    // Gen 1 trainer prize money is roughly base × level; these stand in for "base" per battle tier (Boss pays
    // out the most). Treasure/Mystery aren't battles, so they get their own flat bases below.
    private const int WildGoldBase = 4;
    private const int EliteGoldBase = 8;
    private const int BossGoldBase = 16;

    private const int TreasureGoldBase = 30;
    private const int MysteryGoldBase = 15;

    /// <summary>A battle win's chance to drop anything at all — generous (per the design brief: "a low amount
    /// almost always found").</summary>
    private const double BattleDropChance = 0.85;

    /// <summary>The categories a reward can hand out — mirrors <see cref="creaturegame.Combat.ItemEffects"/>'s
    /// registry (the same set <c>bag.ts USABLE_CATEGORIES</c> hardcodes client-side): Ball/Revive have no
    /// in-battle effect yet, so they're dead loot and never eligible here.</summary>
    private static readonly ItemCategory[] EligibleCategories =
    [
        ItemCategory.Healing,
        ItemCategory.StatusCure,
        ItemCategory.PpRestore,
        ItemCategory.BattleStatBoost,
    ];

    public static IReadOnlyList<Item> UsableItems(IReadOnlyList<Item> allItems) =>
        allItems.Where(i => EligibleCategories.Contains(i.Category)).ToList();

    /// <summary>A battle win's reward: gold most of the time (skewed low — rarely a big haul), an item rarely
    /// (skewed toward cheap/common; Boss wins get a second roll). Can be <see cref="RunReward.Empty"/>.</summary>
    public static RunReward RollBattleReward(
        int enemyLevel,
        RunNodeKind tier,
        IReadOnlyList<Item> usableItems,
        IRandomSource rng
    )
    {
        if (rng.NextDouble() >= BattleDropChance)
            return RunReward.Empty;

        int gold = RollGold(GoldBaseFor(tier), enemyLevel, rng);
        var items = new List<RewardedItem>();
        if (TryPickItem(usableItems, rng) is { } item)
            items.Add(item);
        if (tier == RunNodeKind.BossBattle && TryPickItem(usableItems, rng) is { } bonus)
            items.Add(bonus);

        return gold <= 0 && items.Count == 0 ? RunReward.Empty : new RunReward(gold, items);
    }

    /// <summary>A Treasure node's reward: guaranteed gold plus at least one item — richer than a battle drop
    /// (per the design brief: Treasure is a chest, not a chance).</summary>
    public static RunReward RollTreasureReward(
        int depth,
        IReadOnlyList<Item> usableItems,
        IRandomSource rng
    )
    {
        int gold = RollGold(TreasureGoldBase, DepthLevelProxy(depth), rng);
        var items = new List<RewardedItem>();
        if (TryPickItem(usableItems, rng) is { } first)
            items.Add(first);
        if (rng.NextDouble() < 0.3 && TryPickItem(usableItems, rng) is { } second)
            items.Add(second);

        // A chest is never empty-handed even if the item rolls whiff (an empty item pool at low depth) —
        // guarantee at least the gold.
        return new RunReward(Math.Max(gold, 1), items);
    }

    /// <summary>A Mystery node's reward: the wildcard — gold, an item, both, or (rarely) nothing.</summary>
    public static RunReward RollMysteryReward(
        int depth,
        IReadOnlyList<Item> usableItems,
        IRandomSource rng
    )
    {
        int gold =
            rng.NextDouble() < 0.6 ? RollGold(MysteryGoldBase, DepthLevelProxy(depth), rng) : 0;
        var items = new List<RewardedItem>();
        if (rng.NextDouble() < 0.4 && TryPickItem(usableItems, rng) is { } item)
            items.Add(item);

        return gold <= 0 && items.Count == 0 ? RunReward.Empty : new RunReward(gold, items);
    }

    private static int GoldBaseFor(RunNodeKind tier) =>
        tier switch
        {
            RunNodeKind.EliteBattle => EliteGoldBase,
            RunNodeKind.BossBattle => BossGoldBase,
            _ => WildGoldBase,
        };

    // Treasure/Mystery have no foe to scale off; the run's progression depth stands in for "level" so their
    // payout still climbs as the run goes deeper (mirrors EncounterFactory.ScaleWildLevel's depth axis).
    private static int DepthLevelProxy(int depth) => 5 + Math.Max(0, depth) * 2;

    // base × level × skew, skew = min of two uniforms (low-biased: usually ≈0.5–1.2×, rarely spikes toward 3×)
    // — "a low amount almost always found, a high amount rare" per the design brief.
    // NOTE: every magic number here (the 0.5–3.0 skew window, the /10.0 divisor, the *Base constants) is
    // provisional roguelite balance tuning, expected to be retuned by playtesting — none is load-bearing for
    // correctness (the tests assert only the *shape*: gold climbs with tier/level and the skew is low-biased).
    private static int RollGold(int baseAmount, int level, IRandomSource rng)
    {
        double u = Math.Min(rng.NextDouble(), rng.NextDouble());
        double skew = 0.5 + u * 2.5; // [0.5, 3.0), weighted toward the low end
        return Math.Max(1, (int)Math.Round(baseAmount * level * skew / 10.0));
    }

    // Weighted by inverse cost (cheap items common, expensive ones — Hyper Potion/Full Restore/Ethers/X-items
    // — rare); null if the pool is empty. IReadOnlyList indexing keeps this allocation-free beyond the roll.
    private static RewardedItem? TryPickItem(IReadOnlyList<Item> pool, IRandomSource rng)
    {
        if (pool.Count == 0)
            return null;

        double[] weights = pool.Select(i => 1.0 / (i.Cost + 1)).ToArray();
        double total = weights.Sum();
        double roll = rng.NextDouble() * total;
        double cumulative = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return new RewardedItem(pool[i].Id, pool[i].Name ?? "");
        }
        var last = pool[^1];
        return new RewardedItem(last.Id, last.Name ?? "");
    }
}
