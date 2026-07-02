using creaturegame.Combat;
using creaturegame.Items;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The run economy's reward policy (<see cref="RewardCalculator"/>) — the concrete drop rates / gold curve /
/// item eligibility behind <see cref="RunDirector"/>'s injected reward supplier. Run-layer roguelite tuning,
/// unit-tested like <see cref="EncounterFactory.ScaleWildLevel"/>: pins the shape (gold scales with tier and
/// level, the skew is low-biased, items only ever come from usable categories) without over-fitting exact
/// numbers, plus seeded reproducibility.
/// </summary>
public class RewardCalculatorTests
{
    private static Item MakeItem(int id, ItemCategory category, int cost) =>
        new()
        {
            Id = id,
            Name = $"item{id}",
            Category = category,
            Cost = cost,
        };

    // A catalog spanning every category, including the two dead-loot ones (Ball / Revive have no in-battle
    // effect yet, so a reward must never hand them out).
    private static IReadOnlyList<Item> FullCatalog() =>
        [
            MakeItem(1, ItemCategory.Healing, 300),
            MakeItem(2, ItemCategory.Healing, 1200),
            MakeItem(3, ItemCategory.StatusCure, 100),
            MakeItem(4, ItemCategory.PpRestore, 3000),
            MakeItem(5, ItemCategory.BattleStatBoost, 500),
            MakeItem(6, ItemCategory.Ball, 200),
            MakeItem(7, ItemCategory.Revive, 1500),
            MakeItem(8, ItemCategory.Other, 50),
        ];

    [Fact]
    public void UsableItems_KeepsOnlyEffectCategories_ExcludesBallReviveAndOther()
    {
        var usable = RewardCalculator.UsableItems(FullCatalog());

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, usable.Select(i => i.Id).OrderBy(x => x).ToArray());
        Assert.DoesNotContain(usable, i => i.Category is ItemCategory.Ball or ItemCategory.Revive);
    }

    [Fact]
    public void RollBattleReward_GoldClimbsWithTier_AtTheSameLevel()
    {
        // Empty item pool → the only RNG draws are the drop-chance + gold uniforms, isolating gold. Same seed
        // stream per tier so the comparison is like-for-like; Boss (base 16) must out-pay Elite (8) and Wild (4).
        long wild = TotalGold(RunNodeKind.WildBattle, level: 30);
        long elite = TotalGold(RunNodeKind.EliteBattle, level: 30);
        long boss = TotalGold(RunNodeKind.BossBattle, level: 30);

        Assert.True(elite > wild, $"Elite {elite} should out-pay Wild {wild}");
        Assert.True(boss > elite, $"Boss {boss} should out-pay Elite {elite}");
    }

    [Fact]
    public void RollBattleReward_GoldClimbsWithEnemyLevel()
    {
        long low = TotalGold(RunNodeKind.WildBattle, level: 10);
        long high = TotalGold(RunNodeKind.WildBattle, level: 80);

        Assert.True(high > low, $"L80 gold {high} should exceed L10 gold {low}");
    }

    // Sum of gold over a fixed seeded stream (drop-chance zeros included), item pool empty to isolate gold.
    private static long TotalGold(RunNodeKind tier, int level)
    {
        var rng = new SeededRandomSource(4242);
        long total = 0;
        for (int i = 0; i < 2000; i++)
            total += RewardCalculator.RollBattleReward(level, tier, [], rng).Gold;
        return total;
    }

    [Fact]
    public void RollBattleReward_GoldSkew_IsLowBiased()
    {
        // The gold multiplier is min-of-two-uniforms (bottom-heavy), so over a stream far more non-zero rolls
        // land in the lower half of the observed range than the upper — "a low amount almost always, a high
        // amount rare". Assert the distribution is genuinely bottom-heavy rather than pinning the exact skew.
        var rng = new SeededRandomSource(99);
        var golds = new List<int>();
        for (int i = 0; i < 5000; i++)
        {
            int g = RewardCalculator.RollBattleReward(50, RunNodeKind.BossBattle, [], rng).Gold;
            if (g > 0)
                golds.Add(g);
        }

        Assert.NotEmpty(golds);
        double midpoint = (golds.Min() + golds.Max()) / 2.0;
        int below = golds.Count(g => g < midpoint);
        int above = golds.Count(g => g >= midpoint);
        Assert.True(
            below > above * 2,
            $"expected bottom-heavy gold: {below} below vs {above} above midpoint {midpoint}"
        );
    }

    [Fact]
    public void RollBattleReward_OnlyGrantsItemsFromTheUsablePool()
    {
        // Feed the already-filtered usable pool; every item a battle reward grants must be one of them (so with
        // UsableItems excluding Ball/Revive upstream, a reward can never hand out dead loot).
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var usableIds = usable.Select(i => i.Id).ToHashSet();
        var rng = new SeededRandomSource(7);

        for (int i = 0; i < 3000; i++)
        {
            var reward = RewardCalculator.RollBattleReward(40, RunNodeKind.BossBattle, usable, rng);
            foreach (var item in reward.Items)
                Assert.Contains(item.ItemId, usableIds);
        }
    }

    [Fact]
    public void RollBattleReward_IsReproducibleFromSeed()
    {
        var usable = RewardCalculator.UsableItems(FullCatalog());

        var a = RewardCalculator.RollBattleReward(
            40,
            RunNodeKind.EliteBattle,
            usable,
            new SeededRandomSource(123)
        );
        var b = RewardCalculator.RollBattleReward(
            40,
            RunNodeKind.EliteBattle,
            usable,
            new SeededRandomSource(123)
        );

        Assert.Equal(a.Gold, b.Gold);
        Assert.Equal(a.Items.Select(i => i.ItemId), b.Items.Select(i => i.ItemId));
    }

    [Fact]
    public void RollTreasureReward_IsNeverEmpty_HasGoldAndUsuallyAnItem()
    {
        // A Treasure is a chest, not a chance: guaranteed gold, and at least one item whenever the pool has one.
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var rng = new SeededRandomSource(2024);

        int withItem = 0;
        for (int i = 0; i < 500; i++)
        {
            var reward = RewardCalculator.RollTreasureReward(depth: 3, usable, rng);
            Assert.True(reward.Gold >= 1, "Treasure always pays gold");
            if (reward.Items.Count > 0)
                withItem++;
        }
        Assert.True(
            withItem > 400,
            $"Treasure should almost always include an item ({withItem}/500)"
        );
    }

    [Fact]
    public void RollMysteryReward_IsReproducibleFromSeed()
    {
        var usable = RewardCalculator.UsableItems(FullCatalog());

        var a = RewardCalculator.RollMysteryReward(depth: 5, usable, new SeededRandomSource(555));
        var b = RewardCalculator.RollMysteryReward(depth: 5, usable, new SeededRandomSource(555));

        Assert.Equal(a.Gold, b.Gold);
        Assert.Equal(a.Items.Select(i => i.ItemId), b.Items.Select(i => i.ItemId));
    }
}
