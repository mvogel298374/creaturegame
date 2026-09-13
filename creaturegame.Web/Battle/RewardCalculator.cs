using creaturegame.Combat;
using creaturegame.Items;

namespace creaturegame.Web.Battle;

/// <summary>
/// The reward policy behind the run's injected reward supplier (<see cref="EncounterFactory.BuildRewardSupplier"/>)
/// — drop rates, rarity, gold, item eligibility. Run-layer roguelite tuning, not a battle seam; pure and
/// <c>internal static</c> like <see cref="EncounterFactory.ScaleWildLevel"/>, tested only on shape, never exact
/// numbers. Full mechanics (pick-one-of-N shape, rarity bands, gold formula, Boss category bias, Quick Heal) →
/// <c>ENCOUNTER_DESIGN.md §5.1</c>.
/// </summary>
internal static class RewardCalculator
{
    // Gold bases per tier — Gen 1 trainer prize money is roughly base × level (ENCOUNTER_DESIGN.md §5.1).
    private const int WildGoldBase = 4;
    private const int EliteGoldBase = 8;
    private const int BossGoldBase = 16;
    private const int TreasureGoldBase = 30;
    private const int MysteryGoldBase = 15;

    private const double BattleDropChance = 0.85;
    private const double MysteryRewardChance = 0.7;

    // Quick Heal tuning (§5.1) — provisional balance knobs, tests assert shape only.
    private const double HealBaseChance = 0.10;
    private const double HealHpNeedWeight = 0.7;
    private const double HealStatusNeedBonus = 0.35;
    private const double HealLowPpNeedBonus = 0.15;
    private const double HealMaxChance = 0.9;
    private const double LowPpThreshold = 0.5;
    private const string QuickHealLabel = "Quick Heal";

    // The eligible loot categories (mirrors ItemEffects — Ball has no in-battle effect, so it's excluded).
    // Revive is eligible here but restricted to Boss nodes by RollItemOption — see §5.1.
    private static readonly ItemCategory[] EligibleCategories =
    [
        ItemCategory.Healing,
        ItemCategory.StatusCure,
        ItemCategory.PpRestore,
        ItemCategory.BattleStatBoost,
        ItemCategory.Revive,
    ];

    /// <summary>The obtainable subset of the run's item catalog: eligible categories, nothing else. No
    /// generation hold-out on top — the catalog is already the run generation's content (<c>DATA_IMPORT.md</c>
    /// §4.5).</summary>
    public static IReadOnlyList<Item> UsableItems(IReadOnlyList<Item> allItems) =>
        allItems.Where(i => EligibleCategories.Contains(i.Category)).ToList();

    /// <summary>
    /// The single entry point for the run's reward supplier: rolls the reward for the earning node and returns
    /// the pick-one-of-N <see cref="RewardChoice"/> (or <see cref="RewardChoice.None"/> when nothing rolled).
    /// Dispatches by <see cref="RewardContext.Source"/> — a wild/elite win is gated by the drop chance, a Boss is
    /// guaranteed, a Treasure is a guaranteed chest, a Mystery is the wildcard.
    /// </summary>
    public static RewardChoice RollRewardChoice(
        RewardContext ctx,
        IReadOnlyList<Item> usableItems,
        IRandomSource rng
    )
    {
        switch (ctx.Source)
        {
            case RunNodeKind.BossBattle:
                // A Boss always rewards, and richly — no drop-chance gate.
                return BuildChoice(
                    ctx.Source,
                    ctx.EnemyLevel,
                    ctx.Depth,
                    usableItems,
                    ctx.Condition,
                    rng
                );

            case RunNodeKind.Treasure:
                // A chest is never empty-handed — guaranteed, scaled by run depth (no foe level to scale off).
                return BuildChoice(
                    ctx.Source,
                    DepthLevelProxy(ctx.Depth),
                    ctx.Depth,
                    usableItems,
                    ctx.Condition,
                    rng
                );

            case RunNodeKind.Mystery:
                if (rng.NextDouble() >= MysteryRewardChance)
                    return RewardChoice.None; // the wildcard downside — sometimes nothing
                return BuildChoice(
                    ctx.Source,
                    DepthLevelProxy(ctx.Depth),
                    ctx.Depth,
                    usableItems,
                    ctx.Condition,
                    rng
                );

            default: // WildBattle / EliteBattle — a chance at a drop, not a guarantee
                if (rng.NextDouble() >= BattleDropChance)
                    return RewardChoice.None;
                return BuildChoice(
                    ctx.Source,
                    ctx.EnemyLevel,
                    ctx.Depth,
                    usableItems,
                    ctx.Condition,
                    rng
                );
        }
    }

    // Assembles the pick-one-of-N (§5.1); narrows gracefully (one item + gold, or gold-only) if the pool can't
    // yield two distinct items — never an empty choice, since the node already decided to reward.
    private static RewardChoice BuildChoice(
        RunNodeKind tier,
        int level,
        int depth,
        IReadOnlyList<Item> usable,
        PlayerCondition? condition,
        IRandomSource rng
    )
    {
        var options = new List<RewardOption>();
        var first = RollItemOption(usable, tier, depth, rng, excludeId: null);
        if (first is not null)
            options.Add(first);

        // Quick Heal can take the second item slot instead (§5.1); exempt on Boss (redundant next to the
        // post-Boss Poké Center).
        var heal = tier == RunNodeKind.BossBattle ? null : TryRollHeal(condition, rng);
        if (heal is not null)
        {
            options.Add(heal);
        }
        else
        {
            var second = RollItemOption(usable, tier, depth, rng, excludeId: first?.ItemId);
            if (second is not null)
                options.Add(second);
        }

        var bestRarity = options
            .OfType<ItemRewardOption>()
            .Select(o => o.Rarity)
            .DefaultIfEmpty(RewardRarity.Common)
            .Max();
        options.Add(new GoldRewardOption(RollGoldBag(GoldBaseFor(tier), level, bestRarity, rng)));

        return new RewardChoice(options);
    }

    // Rolls one item option (§5.1); falls back to the whole usable pool if the rolled rarity band is empty, so
    // an option is always found when items exist. Null only for an empty pool.
    private static ItemRewardOption? RollItemOption(
        IReadOnlyList<Item> usable,
        RunNodeKind tier,
        int depth,
        IRandomSource rng,
        int? excludeId
    )
    {
        var pool = usable
            .Where(i => i.Id != excludeId)
            // Revive is a Boss-reward-only drop (the shop draws from the pool directly, not through here).
            .Where(i => tier == RunNodeKind.BossBattle || i.Category != ItemCategory.Revive)
            .ToList();
        if (pool.Count == 0)
            return null;

        var rarity = RollRarity(tier, depth, rng);
        var band = pool.Where(i => RarityOf(i) == rarity).ToList();
        if (band.Count == 0)
            band = pool; // rolled rarity has nothing available → widen to the whole (excluded) pool

        var chosen = WeightedPickByCategory(band, tier, rng);
        return new ItemRewardOption(chosen.Id, chosen.Name ?? "", RarityOf(chosen));
    }

    // --- Quick Heal ------------------------------------------------------------------------------------------

    /// <summary>Rolls the smart-random <see cref="HealRewardOption"/>, or null when one shouldn't be offered
    /// (no condition snapshot, or nothing to heal). Mechanics → <c>ENCOUNTER_DESIGN.md §5.1</c>. <c>internal</c>
    /// for direct unit testing.</summary>
    internal static HealRewardOption? TryRollHeal(PlayerCondition? condition, IRandomSource rng)
    {
        if (condition is not { } c)
            return null;

        int missingHp = Math.Max(0, c.MaxHp - c.CurrentHp);
        bool lowPp = c.LowestPpFraction < LowPpThreshold;
        bool anyNeed = missingHp > 0 || c.HasStatus || lowPp;
        if (!anyNeed)
            return null; // nothing to restore → never offer a useless heal

        double missingHpFrac = c.MaxHp > 0 ? (double)missingHp / c.MaxHp : 0;
        double chance = HealBaseChance + missingHpFrac * HealHpNeedWeight;
        if (c.HasStatus)
            chance += HealStatusNeedBonus;
        if (lowPp)
            chance += HealLowPpNeedBonus;
        if (rng.NextDouble() >= Math.Min(HealMaxChance, chance))
            return null;

        // Restore a random slice of the missing HP — [50%, 100%], so it's always a meaningful heal but varies.
        int hpRestore = 0;
        if (missingHp > 0)
            hpRestore = Math.Max(1, (int)Math.Ceiling(missingHp * (0.5 + 0.5 * rng.NextDouble())));

        return new HealRewardOption(hpRestore, c.HasStatus, lowPp, QuickHealLabel);
    }

    // --- Rarity ---------------------------------------------------------------------------------------------

    /// <summary>Classifies an item into a rarity by its Gen 1 shop cost — bands → <c>ENCOUNTER_DESIGN.md
    /// §5.1</c>. <c>internal</c> for direct unit testing.</summary>
    internal static RewardRarity RarityOf(Item item) =>
        item.Cost switch
        {
            <= 400 => RewardRarity.Common,
            <= 1200 => RewardRarity.Uncommon,
            <= 2500 => RewardRarity.Rare,
            _ => RewardRarity.Epic,
        };

    /// <summary>Rolls a rarity on a per-tier weight table lifted by run depth — <c>ENCOUNTER_DESIGN.md §5.1</c>.
    /// <c>internal</c> for direct unit testing.</summary>
    internal static RewardRarity RollRarity(RunNodeKind tier, int depth, IRandomSource rng)
    {
        double[] w = RarityWeights(tier, depth); // [Common, Uncommon, Rare, Epic]
        double total = w[0] + w[1] + w[2] + w[3];
        double roll = rng.NextDouble() * total;
        double cumulative = 0;
        for (int i = 0; i < 4; i++)
        {
            cumulative += w[i];
            if (roll < cumulative)
                return (RewardRarity)i;
        }
        return RewardRarity.Epic;
    }

    private static double[] RarityWeights(RunNodeKind tier, int depth)
    {
        // Base [Common, Uncommon, Rare, Epic] per tier. Boss is the premium node — Rare/Epic dominant.
        double[] w = tier switch
        {
            RunNodeKind.BossBattle => [20, 30, 35, 15],
            RunNodeKind.Treasure => [40, 35, 20, 5],
            RunNodeKind.EliteBattle => [45, 35, 17, 3],
            RunNodeKind.Mystery => [55, 30, 13, 2],
            _ => [60, 30, 9, 1], // WildBattle
        };

        // Depth lift, capped — monotone, so P(Rare or Epic) never decreases with depth (§5.1).
        double lift = Math.Min(Math.Max(0, depth) * 0.03, 0.6);
        w[3] *= 1 + lift * 3.0; // Epic
        w[2] *= 1 + lift * 2.0; // Rare
        w[0] *= 1 - lift * 0.7; // Common
        return w;
    }

    // --- Category bias (Boss favours replenishment — §5.1) --------------------------------------------------

    private static Item WeightedPickByCategory(
        IReadOnlyList<Item> band,
        RunNodeKind tier,
        IRandomSource rng
    )
    {
        double[] weights = band.Select(i => CategoryWeight(i.Category, tier)).ToArray();
        double total = weights.Sum();
        double roll = rng.NextDouble() * total;
        double cumulative = 0;
        for (int i = 0; i < band.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return band[i];
        }
        return band[^1];
    }

    private static double CategoryWeight(ItemCategory category, RunNodeKind tier)
    {
        if (tier != RunNodeKind.BossBattle)
            return 1.0;

        return category switch
        {
            ItemCategory.Healing => 4.0,
            ItemCategory.Revive => 1.0, // Boss-only already = rare; not up-weighted (§5.1)
            ItemCategory.PpRestore => 2.0,
            ItemCategory.BattleStatBoost => 0.5,
            _ => 1.0,
        };
    }

    // --- Gold ----------------------------------------------------------------------------------------------

    private static int GoldBaseFor(RunNodeKind tier) =>
        tier switch
        {
            RunNodeKind.EliteBattle => EliteGoldBase,
            RunNodeKind.BossBattle => BossGoldBase,
            RunNodeKind.Treasure => TreasureGoldBase,
            RunNodeKind.Mystery => MysteryGoldBase,
            _ => WildGoldBase,
        };

    // Treasure/Mystery have no foe to scale off; run depth stands in for "level" instead.
    private static int DepthLevelProxy(int depth) => 5 + Math.Max(0, depth) * 2;

    /// <summary>The gold-bag option: <c>base × level × skew × rarityFactor</c> — formula → <c>ENCOUNTER_DESIGN.md
    /// §5.1</c>. <c>internal</c> so the rarity-scaling behaviour is directly unit-testable with a fixed seed.
    /// </summary>
    internal static int RollGoldBag(
        int baseAmount,
        int level,
        RewardRarity bestRarity,
        IRandomSource rng
    )
    {
        double u = Math.Min(rng.NextDouble(), rng.NextDouble());
        double skew = 0.5 + u * 2.5; // [0.5, 3.0), weighted toward the low end
        double passiveDrop = baseAmount * level * skew / 10.0;
        double rarityFactor = 1.0 + 0.25 * (int)bestRarity; // Common 1.0 … Epic 1.75
        return Math.Max(1, (int)Math.Round(passiveDrop * 2.0 * rarityFactor));
    }
}
