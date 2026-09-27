using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The run economy's reward policy (<see cref="RewardCalculator"/>) — the drop rates / rarity curve / gold
/// curve / item eligibility behind <see cref="RunDirector"/>'s injected reward supplier. Run-layer roguelite
/// tuning, unit-tested like <see cref="EncounterFactory.ScaleWildLevel"/>: pins the <em>shape</em> (rarity
/// climbs with tier and depth, every option comes from the usable pool, a Boss always rewards and skews toward
/// replenishment, the gold bag is always offered) without over-fitting the provisional weights, plus seeded
/// reproducibility.
/// </summary>
public class RewardCalculatorTests
{
    private static Item MakeItem(int id, ItemCategory category, int cost, string? name = null) =>
        new()
        {
            Id = id,
            Name = name ?? $"item{id}",
            Category = category,
            Cost = cost,
        };

    // A catalog spanning every category, including Ball/Other (still dead loot — no in-battle effect) and Revive
    // (eligible now, but a Boss-reward-only drop — the reward roll further restricts it), across all four bands.
    private static IReadOnlyList<Item> FullCatalog() =>
        [
            MakeItem(1, ItemCategory.Healing, 200), // Common
            MakeItem(2, ItemCategory.Healing, 1500), // Rare
            MakeItem(3, ItemCategory.Healing, 3000), // Epic
            MakeItem(4, ItemCategory.StatusCure, 200), // Common
            MakeItem(5, ItemCategory.PpRestore, 1200), // Uncommon
            MakeItem(6, ItemCategory.BattleStatBoost, 1000), // Uncommon
            MakeItem(7, ItemCategory.Ball, 200),
            MakeItem(8, ItemCategory.Revive, 1500), // Rare band — Boss reward + shop only
            MakeItem(9, ItemCategory.Other, 50),
        ];

    [Fact]
    public void UsableItems_KeepsEffectCategoriesInclRevive_ExcludesBallAndOther()
    {
        // Revive is pool-eligible (it feeds the shop, and Boss rewards); only Ball (catch) and Other stay out.
        var usable = RewardCalculator.UsableItems(FullCatalog());

        Assert.Equal(
            new[] { 1, 2, 3, 4, 5, 6, 8 },
            usable.Select(i => i.Id).OrderBy(x => x).ToArray()
        );
        Assert.DoesNotContain(usable, i => i.Category is ItemCategory.Ball or ItemCategory.Other);
    }

    [Fact]
    public void UsableItems_FiltersByCategoryAlone_NotByItemName()
    {
        // This used to assert a name-matched hold-out on `max-revive`, a Gen-2 item the importer nonetheless
        // imported. Generation Profile Stage 2b removed the cause rather than the symptom: the item is no longer
        // imported and no longer in items.db, and "which content belongs to this generation" is now the content
        // scope's question (IContentScope), asked once when the catalog is loaded. So eligibility here is purely
        // categorical — a second, name-based notion of "not our generation" would be exactly the duplicated
        // source of truth the generation feature keeps deleting.
        IReadOnlyList<Item> catalog =
        [
            MakeItem(1, ItemCategory.Healing, 200),
            MakeItem(62, ItemCategory.Revive, 2000, name: "revive"),
            // A same-category item whose name is unusual: it must still be eligible, because the name is not a
            // filter. Pinned so a name-matched hold-out cannot quietly return.
            MakeItem(63, ItemCategory.Revive, 4000, name: "some-future-revive"),
        ];

        var usable = RewardCalculator.UsableItems(catalog);

        Assert.Equal(new[] { 1, 62, 63 }, usable.Select(i => i.Id).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void RollRewardChoice_OffersRevive_OnBossNodes()
    {
        // Revive (id 8) is a prime Boss reward (category weight 4.0, Rare band) — over many Boss rolls it surfaces.
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var rng = new SeededRandomSource(31);
        bool sawRevive = false;
        for (int i = 0; i < 500 && !sawRevive; i++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(RunNodeKind.BossBattle, EnemyLevel: 40, Depth: 4),
                usable,
                rng
            );
            sawRevive = choice.Options.OfType<ItemRewardOption>().Any(o => o.ItemId == 8);
        }
        Assert.True(sawRevive, "a Boss reward should surface a Revive within many rolls");
    }

    [Theory]
    [InlineData(RunNodeKind.WildBattle)]
    [InlineData(RunNodeKind.EliteBattle)]
    [InlineData(RunNodeKind.Treasure)]
    [InlineData(RunNodeKind.Mystery)]
    public void RollRewardChoice_NeverOffersRevive_OnNonBossNodes(RunNodeKind tier)
    {
        // "Extremely rare" = Boss-reward-only: no non-Boss reward node ever hands out a Revive (id 8).
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var rng = new SeededRandomSource(52);
        for (int i = 0; i < 3000; i++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(tier, EnemyLevel: 40, Depth: 8),
                usable,
                rng
            );
            Assert.DoesNotContain(choice.Options.OfType<ItemRewardOption>(), o => o.ItemId == 8);
        }
    }

    // --- Rarity classification & roll ------------------------------------------------------------------------

    [Theory]
    [InlineData(200, RewardRarity.Common)]
    [InlineData(400, RewardRarity.Common)]
    [InlineData(700, RewardRarity.Uncommon)]
    [InlineData(1200, RewardRarity.Uncommon)]
    [InlineData(1500, RewardRarity.Rare)]
    [InlineData(2500, RewardRarity.Rare)]
    [InlineData(3000, RewardRarity.Epic)]
    [InlineData(4500, RewardRarity.Epic)]
    public void RarityOf_ClassifiesByCostBand(int cost, RewardRarity expected)
    {
        Assert.Equal(expected, RewardCalculator.RarityOf(MakeItem(1, ItemCategory.Healing, cost)));
    }

    [Fact]
    public void RollRarity_SkewsHigherForBetterTiers_AtTheSameDepth()
    {
        // Rarer = more likely on a better node. Over a fixed seeded stream, the share of rolls landing Rare-or-
        // better climbs Wild → Elite → Boss. Pins the ordering, not the exact rates.
        double wild = HighRarityRate(RunNodeKind.WildBattle, depth: 0);
        double elite = HighRarityRate(RunNodeKind.EliteBattle, depth: 0);
        double boss = HighRarityRate(RunNodeKind.BossBattle, depth: 0);

        Assert.True(elite > wild, $"Elite {elite:P0} should out-rank Wild {wild:P0}");
        Assert.True(boss > elite, $"Boss {boss:P0} should out-rank Elite {elite:P0}");
    }

    [Fact]
    public void RollRarity_SkewsHigherWithDepth_AtTheSameTier()
    {
        double shallow = HighRarityRate(RunNodeKind.WildBattle, depth: 0);
        double deep = HighRarityRate(RunNodeKind.WildBattle, depth: 25);

        Assert.True(deep > shallow, $"deep {deep:P0} should out-rank shallow {shallow:P0}");
    }

    // Share of rarity rolls landing Rare or Epic over a fixed seeded stream.
    private static double HighRarityRate(RunNodeKind tier, int depth)
    {
        var rng = new SeededRandomSource(1234);
        int high = 0;
        const int n = 20000;
        for (int i = 0; i < n; i++)
            if (RewardCalculator.RollRarity(tier, depth, rng) >= RewardRarity.Rare)
                high++;
        return (double)high / n;
    }

    // --- The pick-one-of-N choice ---------------------------------------------------------------------------

    [Fact]
    public void RollRewardChoice_AlwaysOffersAGoldBag_AndOnlyUsableItems()
    {
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var usableIds = usable.Select(i => i.Id).ToHashSet();
        var rng = new SeededRandomSource(7);

        for (int i = 0; i < 3000; i++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(RunNodeKind.BossBattle, EnemyLevel: 40, Depth: 3),
                usable,
                rng
            );
            Assert.NotEmpty(choice.Options);
            // Exactly one gold bag, always present (the escape hatch).
            Assert.Single(choice.Options.OfType<GoldRewardOption>());
            Assert.All(choice.Options.OfType<GoldRewardOption>(), g => Assert.True(g.Gold > 0));
            // Every item option is a real usable item, and the two items are distinct.
            var items = choice.Options.OfType<ItemRewardOption>().ToList();
            Assert.All(items, it => Assert.Contains(it.ItemId, usableIds));
            Assert.Equal(items.Count, items.Select(it => it.ItemId).Distinct().Count());
        }
    }

    [Fact]
    public void RollRewardChoice_Boss_AlwaysRewards_WildSometimesWhiffs()
    {
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var rng = new SeededRandomSource(99);

        int bossEmpty = 0,
            wildEmpty = 0;
        for (int i = 0; i < 5000; i++)
        {
            if (
                RewardCalculator
                    .RollRewardChoice(new RewardContext(RunNodeKind.BossBattle, 40, 2), usable, rng)
                    .IsEmpty
            )
                bossEmpty++;
            if (
                RewardCalculator
                    .RollRewardChoice(new RewardContext(RunNodeKind.WildBattle, 40, 2), usable, rng)
                    .IsEmpty
            )
                wildEmpty++;
        }

        Assert.Equal(0, bossEmpty); // a Boss always rewards — no drop-chance gate
        Assert.True(wildEmpty > 0, "a wild win should sometimes whiff (the drop-chance gate)");
    }

    [Fact]
    public void RollRewardChoice_Treasure_NeverWhiffs_AndMysterySometimesDoes()
    {
        // The two non-battle interaction nodes have distinct guarantees: a Treasure chest always rewards (never
        // RewardChoice.None), while a Mystery is the wildcard — it whiffs at ~1 − MysteryRewardChance (0.3).
        // Boss/Wild are covered above; these pin the Treasure/Mystery dispatch arms of RollRewardChoice directly
        // (RunDirectorNodeTests only exercises them via a hand-built RewardChoice, never the real policy).
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var rng = new SeededRandomSource(4321);

        int treasureEmpty = 0,
            mysteryEmpty = 0;
        const int n = 5000;
        for (int i = 0; i < n; i++)
        {
            if (
                RewardCalculator
                    .RollRewardChoice(new RewardContext(RunNodeKind.Treasure, 0, 3), usable, rng)
                    .IsEmpty
            )
                treasureEmpty++;
            if (
                RewardCalculator
                    .RollRewardChoice(new RewardContext(RunNodeKind.Mystery, 0, 3), usable, rng)
                    .IsEmpty
            )
                mysteryEmpty++;
        }

        Assert.Equal(0, treasureEmpty); // a chest is never empty-handed
        // Mystery whiff rate lands near 1 − 0.7 = 0.30 (wide band — pins the wildcard downside, not the exact rate).
        double mysteryWhiffRate = (double)mysteryEmpty / n;
        Assert.InRange(mysteryWhiffRate, 0.25, 0.35);
    }

    [Fact]
    public void RollGoldBag_ScalesWithBestOfferedRarity()
    {
        // The headline "pass up a stronger item and the gold bag pays more" behaviour. Same seed for both calls
        // so the low-biased skew draw is identical — isolating the rarity factor — so an Epic-best bag must beat
        // a Common-best one at equal base/level.
        int commonBag = RewardCalculator.RollGoldBag(
            baseAmount: 16,
            level: 40,
            RewardRarity.Common,
            new SeededRandomSource(77)
        );
        int epicBag = RewardCalculator.RollGoldBag(
            baseAmount: 16,
            level: 40,
            RewardRarity.Epic,
            new SeededRandomSource(77)
        );

        Assert.True(
            epicBag > commonBag,
            $"an Epic-best gold bag ({epicBag}) should beat a Common-best one ({commonBag}) at equal base/level/seed"
        );
    }

    [Fact]
    public void RollRewardChoice_Boss_SkewsItemsTowardReplenishment_UnlikeAWildWin()
    {
        // A band of equally-priced Healing vs BattleStatBoost items (so cost/rarity can't differentiate them):
        // a Boss's category bias should surface Healing far more than stat items, while a wild win — uniform
        // within the band — offers them at roughly even rates. Pins the "Boss leans max-heals" addendum.
        IReadOnlyList<Item> pool =
        [
            MakeItem(1, ItemCategory.Healing, 1500),
            MakeItem(2, ItemCategory.Healing, 1500),
            MakeItem(3, ItemCategory.Healing, 1500),
            MakeItem(4, ItemCategory.BattleStatBoost, 1500),
            MakeItem(5, ItemCategory.BattleStatBoost, 1500),
            MakeItem(6, ItemCategory.BattleStatBoost, 1500),
        ];

        var (bossHeal, bossStat) = CountItemCategories(RunNodeKind.BossBattle, pool);
        var (wildHeal, wildStat) = CountItemCategories(RunNodeKind.WildBattle, pool);

        // Boss: replenishment dominates (weight 4.0 Healing vs 0.5 BattleStatBoost).
        Assert.True(
            bossHeal > bossStat * 2,
            $"Boss should skew to Healing: {bossHeal} heal vs {bossStat} stat"
        );
        // Wild: no category bias, so the two are close (Healing not more than ~1.5× the stat items).
        Assert.True(
            wildHeal < wildStat * 2,
            $"Wild should be roughly even: {wildHeal} heal vs {wildStat} stat"
        );
    }

    private static (int heal, int stat) CountItemCategories(
        RunNodeKind tier,
        IReadOnlyList<Item> pool
    )
    {
        var byId = pool.ToDictionary(i => i.Id);
        var rng = new SeededRandomSource(2024);
        int heal = 0,
            stat = 0;
        for (int i = 0; i < 4000; i++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(tier, EnemyLevel: 40, Depth: 0),
                pool,
                rng
            );
            foreach (var item in choice.Options.OfType<ItemRewardOption>())
            {
                var cat = byId[item.ItemId].Category;
                if (cat == ItemCategory.Healing)
                    heal++;
                else if (cat == ItemCategory.BattleStatBoost)
                    stat++;
            }
        }
        return (heal, stat);
    }

    [Fact]
    public void RollRewardChoice_WithSingleItemPool_StillOffersItemPlusGold()
    {
        // Only one usable item → the choice can't offer two distinct items; it narrows to that item + the gold
        // bag (never an empty or duplicate choice).
        IReadOnlyList<Item> pool = [MakeItem(1, ItemCategory.Healing, 200)];
        var rng = new SeededRandomSource(5);

        var choice = RewardCalculator.RollRewardChoice(
            new RewardContext(RunNodeKind.BossBattle, 40, 1),
            pool,
            rng
        );

        Assert.Equal(2, choice.Options.Count);
        Assert.Single(choice.Options.OfType<ItemRewardOption>());
        Assert.Single(choice.Options.OfType<GoldRewardOption>());
    }

    [Fact]
    public void RollRewardChoice_IsReproducibleFromSeed()
    {
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var ctx = new RewardContext(RunNodeKind.BossBattle, EnemyLevel: 40, Depth: 4);

        var a = RewardCalculator.RollRewardChoice(ctx, usable, new SeededRandomSource(123));
        var b = RewardCalculator.RollRewardChoice(ctx, usable, new SeededRandomSource(123));

        Assert.Equal(Describe(a), Describe(b));
    }

    // A stable string signature of a choice's options, for seeded-reproducibility comparison.
    private static string Describe(RewardChoice choice) =>
        string.Join(
            "|",
            choice.Options.Select(o =>
                o switch
                {
                    ItemRewardOption i => $"item:{i.ItemId}:{i.Rarity}",
                    GoldRewardOption g => $"gold:{g.Gold}",
                    _ => "?",
                }
            )
        );

    // --- Quick Heal (smart-random) -------------------------------------------------------------------------

    private static PlayerCondition Cond(
        int cur,
        int max,
        bool status = false,
        double lowPp = 1.0
    ) => new(cur, max, status, lowPp);

    [Fact]
    public void TryRollHeal_ReturnsNull_WithoutCondition()
    {
        // Condition-less callers (the legacy chain, existing tests) never see a heal option.
        Assert.Null(RewardCalculator.TryRollHeal(null, new SeededRandomSource(1)));
    }

    [Fact]
    public void TryRollHeal_ReturnsNull_WhenFullyHealthy()
    {
        // Full HP, no status, full PP → nothing to restore → never a dead option, whatever the roll.
        var healthy = Cond(50, 50);
        for (int seed = 0; seed < 50; seed++)
            Assert.Null(RewardCalculator.TryRollHeal(healthy, new SeededRandomSource(seed)));
    }

    [Fact]
    public void TryRollHeal_FiresForAStrongMajority_WhenBadlyHurt()
    {
        // Smart: a badly-hurt creature is offered the heal far more often than a healthy one (which is never).
        var hurt = Cond(3, 50);
        var rng = new SeededRandomSource(99);
        int fired = 0;
        for (int i = 0; i < 400; i++)
            if (RewardCalculator.TryRollHeal(hurt, rng) is not null)
                fired++;
        Assert.True(fired > 250, $"expected a strong majority when badly hurt, got {fired}/400");
    }

    [Fact]
    public void TryRollHeal_HpRestoreNeverExceedsMissing()
    {
        var hurt = Cond(40, 50); // 10 missing
        var rng = new SeededRandomSource(7);
        for (int i = 0; i < 400; i++)
        {
            var heal = RewardCalculator.TryRollHeal(hurt, rng);
            if (heal is not null)
                Assert.InRange(heal.HpRestore, 1, 10);
        }
    }

    [Fact]
    public void TryRollHeal_SetsOnlyTheApplicableComponents()
    {
        // Statused + low PP but full HP → HP component off, status + PP on (adaptive, not a blanket heal).
        var rng = new SeededRandomSource(3);
        HealRewardOption? heal = null;
        for (int i = 0; i < 100 && heal is null; i++)
            heal = RewardCalculator.TryRollHeal(Cond(50, 50, status: true, lowPp: 0.1), rng);

        Assert.NotNull(heal);
        Assert.Equal(0, heal!.HpRestore); // full HP → no HP restore
        Assert.True(heal.CureStatus);
        Assert.True(heal.RestoreLowPp);
    }

    [Fact]
    public void RollRewardChoice_CanOfferQuickHeal_WhenConditionShowsNeed()
    {
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var needy = Cond(2, 50, status: true, lowPp: 0.1);
        var rng = new SeededRandomSource(11);
        bool sawHeal = false;
        for (int i = 0; i < 200 && !sawHeal; i++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(RunNodeKind.Treasure, 0, 2, needy),
                usable,
                rng
            );
            sawHeal = choice.Options.OfType<HealRewardOption>().Any();
        }
        Assert.True(
            sawHeal,
            "a needy player should be offered a Quick Heal within many reward rolls"
        );
    }

    [Fact]
    public void RollRewardChoice_NeverOffersQuickHeal_OnBossNodes_EvenWhenNeedy()
    {
        // Boss rewards stay elevated (all items/gold) and a full-heal Poké Center follows the Boss anyway, so a
        // Boss node never rolls Quick Heal — even for a badly-hurt creature that would trigger it elsewhere.
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var needy = Cond(1, 50, status: true, lowPp: 0.05);
        var rng = new SeededRandomSource(77);
        for (int i = 0; i < 300; i++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(RunNodeKind.BossBattle, 40, 3, needy),
                usable,
                rng
            );
            Assert.Empty(choice.Options.OfType<HealRewardOption>());
        }
    }

    [Fact]
    public void RollRewardChoice_NeverOffersQuickHeal_WithoutCondition()
    {
        // Existing condition-less behaviour is preserved: no heal option ever leaks into those choices.
        var usable = RewardCalculator.UsableItems(FullCatalog());
        var rng = new SeededRandomSource(4);
        for (int i = 0; i < 200; i++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(RunNodeKind.Treasure, 0, 2),
                usable,
                rng
            );
            Assert.Empty(choice.Options.OfType<HealRewardOption>());
        }
    }

    // --- Move-teach (TM/HM — Move-Teach Rewards) -------------------------------------------------------------

    private static Attack MakeMove(int id, string name, DamageType type, int power = 40) =>
        new()
        {
            Id = id,
            Name = name,
            DamageType = type,
            BaseDamage = power,
            Accuracy = 100,
            PowerPointsMax = 15,
        };

    private static Creature MakeMember(string name, int speciesId, DamageType type1) =>
        new(name)
        {
            SpeciesId = speciesId,
            Type1 = type1,
            Level = 20,
        };

    [Fact]
    public void RollMoveTeachOption_ReturnsNull_WhenTheDictionaryIsEmpty()
    {
        var party = new Party(MakeMember("CHARMANDER", 4, DamageType.Fire));
        var moves = new[] { MakeMove(7, "Flamethrower", DamageType.Fire, 95) };

        var option = RewardCalculator.RollMoveTeachOption(
            party,
            moves,
            new Dictionary<int, IReadOnlyList<int>>(),
            new SeededRandomSource(1)
        );

        Assert.Null(option);
    }

    [Fact]
    public void RollMoveTeachOption_NeverOffersAMove_WithNoMachineRowForTheSpecies()
    {
        // The legality dictionary carries a real move for a DIFFERENT species (id 5) than the one in the party
        // (id 4) — the quirk this pins: a species' own Gen 1 Machine learnset is the only door in, so no
        // candidate is ever built from another species' legality leaking across.
        var party = new Party(MakeMember("CHARMANDER", 4, DamageType.Fire));
        var moves = new[] { MakeMove(7, "Flamethrower", DamageType.Fire, 95) };
        var machineMoves = new Dictionary<int, IReadOnlyList<int>> { [5] = [7] };

        var option = RewardCalculator.RollMoveTeachOption(
            party,
            moves,
            machineMoves,
            new SeededRandomSource(1)
        );

        Assert.Null(option);
    }

    [Fact]
    public void RollMoveTeachOption_ExcludesAMemberThatAlreadyKnowsTheMove()
    {
        var member = MakeMember("CHARMANDER", 4, DamageType.Fire);
        var move = MakeMove(7, "Flamethrower", DamageType.Fire, 95);
        member.AddAttack(move); // already knows it — legal, but not a fresh teach
        var party = new Party(member);
        var machineMoves = new Dictionary<int, IReadOnlyList<int>> { [4] = [7] };

        var option = RewardCalculator.RollMoveTeachOption(
            party,
            [move],
            machineMoves,
            new SeededRandomSource(1)
        );

        Assert.Null(option); // the only member in range is already-knows, so no candidate at all
    }

    [Fact]
    public void RollMoveTeachOption_TrustsWhateverLegalityDictionaryItsGiven_HmExclusionIsUpstream()
    {
        // The HM exclusion moved to EncounterFactory.LoadMachineLearnsetsAsync (off GenerationProfile.HmMoveIds
        // — see EncounterFactoryGenerationProfileTests for that falsification leg), so RollMoveTeachOption no
        // longer filters by move id itself — it trusts the dictionary it's handed. This documents that contract
        // rather than re-testing an exclusion that no longer lives here: a dictionary entry (even Surf, a real
        // Gen 1 HM) is offered exactly as given.
        var squirtle = MakeMember("SQUIRTLE", 7, DamageType.Water);
        var party = new Party(squirtle);
        var surf = MakeMove(57, "Surf", DamageType.Water, 90);
        var machineMoves = new Dictionary<int, IReadOnlyList<int>> { [7] = [57] };

        var option = RewardCalculator.RollMoveTeachOption(
            party,
            [surf],
            machineMoves,
            new SeededRandomSource(1)
        );

        Assert.NotNull(option);
        Assert.Equal(surf.Id, option!.Move.Id);
    }

    [Fact]
    public void RollMoveTeachOption_MarksOnlyTheEligibleMembersSlotAble()
    {
        // Slot 0 (Charmander) can legally learn Flamethrower and doesn't know it yet — ABLE. Slot 1 (Bulbasaur)
        // has no Machine row for it at all — NOT ABLE, a different reason than "already knows it" but the same
        // false verdict.
        var charmander = MakeMember("CHARMANDER", 4, DamageType.Fire);
        var bulbasaur = MakeMember("BULBASAUR", 1, DamageType.Grass);
        var party = new Party(charmander);
        party.Add(bulbasaur);
        var move = MakeMove(7, "Flamethrower", DamageType.Fire, 95);
        var machineMoves = new Dictionary<int, IReadOnlyList<int>> { [4] = [7] };

        var option = RewardCalculator.RollMoveTeachOption(
            party,
            [move],
            machineMoves,
            new SeededRandomSource(1)
        );

        Assert.NotNull(option);
        Assert.Equal(move.Id, option!.Move.Id);
        Assert.True(option.AbleBySlot[0]);
        Assert.False(option.AbleBySlot[1]);
    }

    // --- Gold-rarity scaling for a move-teach pick (requirements-review, 2026-09-28) ------------------------

    [Fact]
    public void ResolveBestRarity_TreatsMoveTeachAsRare_WhenNoItemSurvives()
    {
        var move = MakeMove(7, "Flamethrower", DamageType.Fire, 95);
        var options = new List<RewardOption> { new MoveTeachRewardOption(move, [true]) };

        Assert.Equal(RewardRarity.Rare, RewardCalculator.ResolveBestRarity(options));
    }

    [Fact]
    public void ResolveBestRarity_AnEpicItemStillBeatsAMoveTeachsRareFloor()
    {
        var move = MakeMove(7, "Flamethrower", DamageType.Fire, 95);
        var options = new List<RewardOption>
        {
            new MoveTeachRewardOption(move, [true]),
            new ItemRewardOption(1, "Full Restore", RewardRarity.Epic),
        };

        Assert.Equal(RewardRarity.Epic, RewardCalculator.ResolveBestRarity(options));
    }

    [Fact]
    public void ResolveBestRarity_FallsBackToCommon_WhenNothingRolled()
    {
        Assert.Equal(RewardRarity.Common, RewardCalculator.ResolveBestRarity([]));
        Assert.Equal(
            RewardRarity.Common,
            RewardCalculator.ResolveBestRarity([new GoldRewardOption(10)])
        );
    }

    // --- Boss Revive-slot protection (requirements-review, 2026-09-28) ---------------------------------------

    [Fact]
    public void RollRewardChoice_OnBoss_MoveTeachNeverDisplacesTheFirstItemSlot()
    {
        // The first Boss item slot is the one Revive can occupy; move-teach must always land in the second slot
        // instead, so a Boss roll's Revive odds are the same whether or not move-teach fires this time.
        var party = new Party(MakeMember("CHARMANDER", 4, DamageType.Fire));
        var moves = new[] { MakeMove(7, "Flamethrower", DamageType.Fire, 95) };
        var machineMoves = new Dictionary<int, IReadOnlyList<int>> { [4] = [7] };
        var usable = RewardCalculator.UsableItems(FullCatalog());
        bool sawMoveTeach = false;

        for (int seed = 0; seed < 200; seed++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(RunNodeKind.BossBattle, EnemyLevel: 40, Depth: 2, Party: party),
                usable,
                new SeededRandomSource(seed),
                moves,
                machineMoves
            );
            int moveTeachIndex = choice.Options.ToList().FindIndex(o => o is MoveTeachRewardOption);
            if (moveTeachIndex < 0)
                continue; // didn't fire this seed

            sawMoveTeach = true;
            Assert.NotEqual(0, moveTeachIndex); // never the first (Revive-eligible) slot
        }

        // At least one of 200 Boss seeds must have hit the 35% move-teach chance with this always-eligible
        // party, or the test would be vacuously true.
        Assert.True(sawMoveTeach);
    }

    [Fact]
    public void RollRewardChoice_OffBoss_MoveTeachAlwaysSitsAtIndexZeroWhenItFires()
    {
        // The Boss slot rule above is Boss-only — every other tier is Revive-ineligible regardless
        // (RollItemOption's own Boss-only gate), so move-teach keeps substituting the first slot there.
        var party = new Party(MakeMember("CHARMANDER", 4, DamageType.Fire));
        var moves = new[] { MakeMove(7, "Flamethrower", DamageType.Fire, 95) };
        var machineMoves = new Dictionary<int, IReadOnlyList<int>> { [4] = [7] };
        var usable = RewardCalculator.UsableItems(FullCatalog());
        bool sawMoveTeach = false;

        for (int seed = 0; seed < 200; seed++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(RunNodeKind.Treasure, EnemyLevel: 0, Depth: 2, Party: party),
                usable,
                new SeededRandomSource(seed),
                moves,
                machineMoves
            );
            int moveTeachIndex = choice.Options.ToList().FindIndex(o => o is MoveTeachRewardOption);
            if (moveTeachIndex < 0)
                continue;

            sawMoveTeach = true;
            Assert.Equal(0, moveTeachIndex);
        }

        Assert.True(sawMoveTeach);
    }

    [Fact]
    public void RollRewardChoice_WithNoEligibleMoveTeachCandidate_AlwaysFallsBackToAnOrdinaryItemRoll()
    {
        // An empty party/legality pairing must never roll a move-teach — it should behave exactly as if the
        // party parameter had been omitted entirely (the existing null-Party tests above).
        var party = new Party(MakeMember("CATERPIE", 10, DamageType.Bug)); // no Machine learnset at all
        var moves = new[] { MakeMove(7, "Flamethrower", DamageType.Fire, 95) };
        var machineMoves = new Dictionary<int, IReadOnlyList<int>>(); // nothing legal for anyone
        var usable = RewardCalculator.UsableItems(FullCatalog());

        for (int seed = 0; seed < 50; seed++)
        {
            var choice = RewardCalculator.RollRewardChoice(
                new RewardContext(RunNodeKind.BossBattle, EnemyLevel: 40, Depth: 2, Party: party),
                usable,
                new SeededRandomSource(seed),
                moves,
                machineMoves
            );
            Assert.Empty(choice.Options.OfType<MoveTeachRewardOption>());
            Assert.Contains(choice.Options, o => o is ItemRewardOption); // the first slot still rolled an item
        }
    }
}
