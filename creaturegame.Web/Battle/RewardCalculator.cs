using creaturegame.Combat;
using creaturegame.Items;

namespace creaturegame.Web.Battle;

/// <summary>
/// The reward policy behind the run's injected reward supplier (<c>RunDirector</c>'s
/// <c>Func&lt;RewardContext, IRandomSource, RewardChoice&gt;</c>) — drop rates, the rarity curve, the gold
/// curve, and item eligibility. Run-layer roguelite tuning, not a battle seam (same class as
/// <see cref="EncounterFactory.ScaleWildLevel"/>): pure and <c>internal static</c> so it's unit-testable
/// exactly like that knob. <see cref="EncounterFactory"/> closes over the usable-item subset and calls
/// <see cref="RollRewardChoice"/>, which dispatches by <c>RewardContext.Source</c>.
///
/// <para>Every rolled reward is offered to the player as a <b>pick-one-of-N</b> (two rarity-rolled items or a
/// larger gold bag). Item value is a two-step roll: a <see cref="RewardRarity"/> on a table biased upward by
/// node tier + run depth (rarer = more expensive), then an item drawn from that rarity's cost band. Boss nodes
/// are special: the rarity table skews hardest to Rare/Epic <em>and</em> a category bias favours replenishment
/// (Healing / PpRestore) over stat items, so a Boss leans max-heals / strong potions — plus a Boss always
/// rolls (no whiff) and pays a fatter gold bag. All magic numbers here are provisional roguelite balance
/// tuning, expected to be retuned by playtest — none is load-bearing for correctness (the tests assert only the
/// <em>shape</em>: rarity climbs with tier/depth, options come from the usable pool, the gold bag beats the
/// passive drop).</para>
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

    /// <summary>A battle win's chance to roll anything at all — generous (per the design brief: "a low amount
    /// almost always found"). A Boss ignores this (it always rewards); a wild/elite whiff → no choice offered.
    /// ⚠ Tuning knob: now that a win pops a modal, this may want lowering so the grind isn't a modal after
    /// nearly every fight.</summary>
    private const double BattleDropChance = 0.85;

    /// <summary>A Mystery node's chance to produce a reward at all (else nothing — the wildcard downside).</summary>
    private const double MysteryRewardChance = 0.7;

    // --- Quick Heal (smart-random) tuning ------------------------------------------------------------------
    // The heal option appears smart-randomly: gated on the creature having *some* need (so it's never a dead
    // option), then a probability that's a small base rate lifted by how badly it needs healing. Its magnitude
    // is randomized too. All provisional balance knobs — the tests assert only the shape (biased to need, HP
    // never exceeds the missing amount), never the exact numbers.
    private const double HealBaseChance = 0.10; // the "also randomly" floor, applied whenever there's any need
    private const double HealHpNeedWeight = 0.7; // + up to this much as HP approaches empty
    private const double HealStatusNeedBonus = 0.35; // + this when statused
    private const double HealLowPpNeedBonus = 0.15; // + this when a move is below the low-PP threshold
    private const double HealMaxChance = 0.9; // cap, so it's never a certainty
    private const double LowPpThreshold = 0.5; // a move at/under half PP counts as "low"
    private const string QuickHealLabel = "Quick Heal";

    /// <summary>The categories a reward can hand out — mirrors <see cref="creaturegame.Combat.ItemEffects"/>'s
    /// registry (the same set <c>bag.ts</c> filters on server-side): Ball/Revive have no in-battle effect yet,
    /// so they're dead loot and never eligible here. <b>Revive is framework-ready</b> in the category bias below
    /// (it would be a prime Boss reward) but stays out of this eligible set until it becomes usable.</summary>
    private static readonly ItemCategory[] EligibleCategories =
    [
        ItemCategory.Healing,
        ItemCategory.StatusCure,
        ItemCategory.PpRestore,
        ItemCategory.BattleStatBoost,
    ];

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

    // Assembles the pick-one-of-N: two distinct rarity-rolled item options plus a gold-bag escape hatch. The
    // gold bag scales with the better of the two item rarities, so passing up a strong item pays more. When the
    // pool can't yield two distinct items (a tiny catalog), the choice narrows gracefully (one item + gold, or
    // gold-only for an empty pool) — never an empty choice, since the node already decided to reward.
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

        // Smart-random Quick Heal: when the creature actually needs it, offer an on-the-spot heal in place of the
        // second item slot — so the choice stays a pick-one-of-N alongside the gold bag. Only surfaces components
        // that apply; never a dead option (TryRollHeal gates on need). Boss nodes are exempt: their item reward is
        // deliberately elevated, and a biome caps with a free full-heal Poké Center right after the Boss, so a
        // heal there is redundant.
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

    // Rolls one item option: a rarity (tier/depth-biased), then an item drawn from that rarity's cost band,
    // weighted by a per-tier category bias (Boss favours replenishment). Falls back to the whole usable pool if
    // the rolled band is empty (or holds only the excluded item), so an option is always found when items exist.
    // Returns null only for an empty pool. The returned rarity is the item's <em>actual</em> band, not the
    // rolled one (they match unless the fallback widened the pool).
    private static ItemRewardOption? RollItemOption(
        IReadOnlyList<Item> usable,
        RunNodeKind tier,
        int depth,
        IRandomSource rng,
        int? excludeId
    )
    {
        var pool = excludeId is { } id ? usable.Where(i => i.Id != id).ToList() : usable.ToList();
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

    /// <summary>
    /// Rolls the smart-random <see cref="HealRewardOption"/>, or null when one shouldn't be offered. Null when
    /// there's no condition snapshot (callers that don't pass one — e.g. tests — never see a heal) or the
    /// creature has nothing to heal (full HP, no status, full PP) — so the heal is never a dead option. When it
    /// does fire, it restores only the components that apply, sized randomly (HP a random slice of the missing
    /// amount, so it always helps but the amount varies). <c>internal</c> so the shape is directly unit-testable.
    /// </summary>
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

    /// <summary>Classifies an item into a rarity by its Gen 1 shop cost — rarer = more expensive. Bands
    /// (≤400 / ≤1200 / ≤2500 / &gt;2500) place the cheap sustain (Potion, status cures) at Common and the
    /// premium restores (Full Restore, Elixirs) at Epic. <c>internal</c> for direct unit testing.</summary>
    internal static RewardRarity RarityOf(Item item) =>
        item.Cost switch
        {
            <= 400 => RewardRarity.Common,
            <= 1200 => RewardRarity.Uncommon,
            <= 2500 => RewardRarity.Rare,
            _ => RewardRarity.Epic,
        };

    /// <summary>Rolls a rarity on a per-tier weight table, lifted upward by run depth. Boss skews hardest to
    /// Rare/Epic (the premium node); Wild is Common-heavy. Deeper runs shift weight up (Epic fastest), so late
    /// nodes surface better loot. Weights are provisional. <c>internal</c> for direct unit testing.</summary>
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

        // Depth lift: as the run deepens, shift weight upward (Epic grows fastest, Common shrinks), capped so a
        // late wild node still isn't all-Epic. Monotone in depth, so P(Rare or Epic) never decreases with depth.
        double lift = Math.Min(Math.Max(0, depth) * 0.03, 0.6);
        w[3] *= 1 + lift * 3.0; // Epic
        w[2] *= 1 + lift * 2.0; // Rare
        w[0] *= 1 - lift * 0.7; // Common
        return w;
    }

    // --- Category bias (Boss favours replenishment) --------------------------------------------------------

    // Weighted pick within a band. For a Boss the weights favour replenishing items (Healing/PpRestore, and
    // Revive once it's usable) over stat items, so a Boss leans max-heals / strong potions; other tiers pick
    // uniformly within the band (weight 1 each). Cost-tie order is irrelevant — the weight is by category.
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

    // A Boss (the premium node) up-weights replenishment and down-weights stat items; every other tier is
    // uniform (weight 1). Revive is weighted like a top Boss reward so it slots straight in the moment it
    // becomes usable — until then it's filtered out of the eligible pool upstream, so this arm is dormant.
    private static double CategoryWeight(ItemCategory category, RunNodeKind tier)
    {
        if (tier != RunNodeKind.BossBattle)
            return 1.0;

        return category switch
        {
            ItemCategory.Healing => 4.0,
            ItemCategory.Revive => 4.0, // dormant: filtered upstream until usable, then a prime Boss reward
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

    // Treasure/Mystery have no foe to scale off; the run's progression depth stands in for "level" so their
    // payout still climbs as the run goes deeper (mirrors EncounterFactory.ScaleWildLevel's depth axis).
    private static int DepthLevelProxy(int depth) => 5 + Math.Max(0, depth) * 2;

    // The gold-bag option: the old passive drop's low-biased roll, then made fatter (×2) so gold is a real
    // alternative to an item, and scaled up by the better offered item's rarity so passing a strong item pays
    // more. base × level × skew (skew = min of two uniforms, low-biased) — "a low amount almost always, a high
    // amount rare" — then the fatten + rarity factors. All magic numbers provisional (see the class note).
    // <c>internal</c> so the rarity-scaling behaviour ("a stronger passed-up item pays more") is directly unit-
    // testable with a fixed seed, isolating the rarity factor from the shared skew draw.
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
