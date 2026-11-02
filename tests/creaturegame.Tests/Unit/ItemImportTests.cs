using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for the importer's Gen 1 item mapping (<see cref="ItemMapper"/>). Pure mapping over a
/// built DTO — no network, no database. Covers the hand-curated Gen 1 roster (PokeAPI has no Gen 1
/// membership signal for items), the category grouping, and the layer-2 Gen 1 gameplay numbers.
/// </summary>
public class ItemImportTests
{
    private static PokeApiItem PokeItem(
        string name,
        string? category = null,
        int id = 1,
        int? flingPower = null
    ) =>
        new()
        {
            Id = id,
            Name = name,
            FlingPower = flingPower,
            Category = category == null ? null : new NamedApiResource { Name = category },
            Sprites = new ItemSprites { Default = $"https://img/{name}.png" },
        };

    // ── Gen 1 roster (hand-curated, since PokeAPI has no Gen 1 item signal) ─────────────────────────

    [Theory]
    [InlineData("poke-ball")]
    [InlineData("master-ball")]
    [InlineData("potion")]
    [InlineData("full-restore")]
    [InlineData("antidote")]
    [InlineData("ether")]
    [InlineData("x-attack")]
    [InlineData("x-sp-atk")] // modern slug for Gen 1's X Special
    public void Gen1Roster_IncludesBattleUsableGen1Items(string slug)
    {
        Assert.Contains(slug, ItemMapper.Gen1BattleItemNames);
    }

    [Theory]
    [InlineData("net-ball")] // Gen 3 ball
    [InlineData("x-sp-def")] // X Sp. Def didn't exist in Gen 1
    [InlineData("fire-stone")] // evolution stone — out of scope
    [InlineData("hp-up")] // vitamin — out of scope
    [InlineData("rare-candy")] // out of scope
    [InlineData("bicycle")] // key item — out of scope
    public void Gen1Roster_ExcludesOutOfScopeItems(string slug)
    {
        Assert.DoesNotContain(slug, ItemMapper.Gen1BattleItemNames);
    }

    // ── Core field mapping ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MapToItem_CopiesCoreFields()
    {
        var item = ItemMapper.MapToItem(
            PokeItem("great-ball", "standard-balls", id: 3, flingPower: 0)
        );

        Assert.Equal(3, item.Id);
        Assert.Equal("great-ball", item.Name);
        Assert.Equal(600, item.Cost);
        Assert.Equal(0, item.FlingPower);
        Assert.Equal("https://img/great-ball.png", item.SpriteUrl);
    }

    [Theory]
    [InlineData("potion", 300)]
    [InlineData("antidote", 100)]
    [InlineData("full-heal", 600)]
    [InlineData("x-accuracy", 950)]
    [InlineData("ether", 0)] // Gen 1 never sold PP items
    [InlineData("master-ball", 0)]
    public void MapToItem_TakesTheGen1Price(string slug, int gen1Price)
    {
        Assert.Equal(gen1Price, ItemMapper.MapToItem(PokeItem(slug)).Cost);
    }

    [Fact]
    public void MapToItem_RejectsAnItemWithNoGen1Price_InsteadOfImportingIt()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ItemMapper.MapToItem(PokeItem("nugget", "healing"))
        );
    }

    [Fact]
    public void Gen1Prices_CoverExactlyTheRoster()
    {
        Assert.Equal(
            ItemMapper.Gen1BattleItemNames.OrderBy(n => n),
            ItemMapper.Gen1Prices.Keys.OrderBy(n => n)
        );
    }

    [Fact]
    public void Gen1Descriptions_CoverExactlyTheRoster()
    {
        Assert.Equal(
            ItemMapper.Gen1BattleItemNames.OrderBy(n => n),
            ItemMapper.Gen1Descriptions.Keys.OrderBy(n => n)
        );
    }

    [Theory]
    [InlineData("x-attack", "Raises Attack by one stage in battle.")]
    [InlineData("x-defense", "Raises Defense by one stage in battle.")]
    [InlineData("x-speed", "Raises Speed by one stage in battle.")]
    [InlineData("x-sp-atk", "Raises Special by one stage in battle.")]
    [InlineData("x-accuracy", "The user's moves skip the accuracy check until it switches out.")]
    [InlineData(
        "dire-hit",
        "Pumps up the user, but a bug usually makes its critical hits a quarter as likely until it switches out."
    )]
    [InlineData(
        "guard-spec",
        "Stops status moves that lower the user's stats until it switches out."
    )]
    public void MapToItem_BattleBoosters_DescribeTheirGen1Effect(string slug, string description)
    {
        Assert.Equal(description, ItemMapper.MapToItem(PokeItem(slug)).Description);
    }

    [Theory]
    [InlineData(
        "full-restore",
        "Restores HP to full and cures any status ailment, but not confusion."
    )]
    [InlineData("full-heal", "Cures any status ailment, but not confusion.")]
    public void MapToItem_FullCures_StateThatConfusionStays(string slug, string description)
    {
        Assert.Equal(description, ItemMapper.MapToItem(PokeItem(slug)).Description);
    }

    [Theory]
    [InlineData("great-ball", "Tries to catch a wild Pokémon, with better odds than a Poké Ball.")]
    [InlineData("ultra-ball", "Tries to catch a wild Pokémon, with better odds than a Poké Ball.")]
    [InlineData(
        "safari-ball",
        "Tries to catch a wild Pokémon, with the same odds as an Ultra Ball."
    )]
    public void MapToItem_Balls_DescribeGen1Odds(string slug, string description)
    {
        Assert.Equal(description, ItemMapper.MapToItem(PokeItem(slug)).Description);
    }

    [Fact]
    public void Gen1Descriptions_CarryNoLaterGenerationEffects()
    {
        Assert.All(
            ItemMapper.Gen1Descriptions.Values,
            text =>
            {
                Assert.DoesNotContain("happiness", text);
                Assert.DoesNotContain("Success rate", text);
                Assert.DoesNotContain("five turns", text);
                Assert.DoesNotContain("Great Marsh", text);
            }
        );
    }

    [Theory]
    [InlineData("standard-balls", ItemCategory.Ball)]
    [InlineData("special-balls", ItemCategory.Ball)]
    [InlineData("healing", ItemCategory.Healing)]
    [InlineData("status-cures", ItemCategory.StatusCure)]
    [InlineData("revival", ItemCategory.Revive)]
    [InlineData("pp-recovery", ItemCategory.PpRestore)]
    [InlineData("stat-boosts", ItemCategory.BattleStatBoost)]
    [InlineData("key-items", ItemCategory.Other)]
    [InlineData(null, ItemCategory.Other)]
    public void MapToItem_MapsCategory(string? pokeApiCategory, ItemCategory expected)
    {
        var item = ItemMapper.MapToItem(PokeItem("potion", pokeApiCategory));
        Assert.Equal(expected, item.Category);
    }

    // ── Layer-2 Gen 1 gameplay numbers ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("potion", 20)]
    [InlineData("super-potion", 50)]
    [InlineData("hyper-potion", 200)]
    public void MapToItem_FixedHealAmounts(string name, int heal)
    {
        var item = ItemMapper.MapToItem(PokeItem(name, "healing"));
        Assert.Equal(heal, item.HealAmount);
        Assert.False(item.HealsAllHp);
    }

    [Fact]
    public void MapToItem_MaxPotion_HealsAllHp()
    {
        var item = ItemMapper.MapToItem(PokeItem("max-potion", "healing"));
        Assert.True(item.HealsAllHp);
        Assert.Null(item.HealAmount);
    }

    [Fact]
    public void MapToItem_FullRestore_HealsAllHpAndCuresStatus()
    {
        var item = ItemMapper.MapToItem(PokeItem("full-restore", "healing"));
        Assert.True(item.HealsAllHp);
        Assert.True(item.CuresAllStatus);
    }

    [Theory]
    [InlineData("antidote", StatusCondition.Poison)]
    [InlineData("burn-heal", StatusCondition.Burn)]
    [InlineData("ice-heal", StatusCondition.Freeze)]
    [InlineData("awakening", StatusCondition.Sleep)]
    [InlineData("paralyze-heal", StatusCondition.Paralysis)]
    public void MapToItem_SingleStatusCures(string name, StatusCondition cured)
    {
        var item = ItemMapper.MapToItem(PokeItem(name, "status-cures"));
        Assert.Equal(cured, item.CuredStatus);
        Assert.False(item.CuresAllStatus);
    }

    [Fact]
    public void MapToItem_FullHeal_CuresAllStatus()
    {
        var item = ItemMapper.MapToItem(PokeItem("full-heal", "status-cures"));
        Assert.True(item.CuresAllStatus);
        Assert.Null(item.CuredStatus);
    }

    [Fact]
    public void MapToItem_Revive_RestoresHalfHp()
    {
        // A Fact, not a Theory: Revive is Gen 1's only revival item. This was a two-case Theory until Max Revive
        // (Gen 2) was dropped from the roster — see the guard below.
        var item = ItemMapper.MapToItem(PokeItem("revive", "revival"));
        Assert.Equal(50, item.RevivePercent);
    }

    [Fact]
    public void Gen1BattleItemNames_ExcludesMaxRevive_TheItemsCatalogIsOneGenerationsContent()
    {
        // Max Revive is Gen 2 (Red/Blue/Yellow shipped Revive alone). It used to be imported as forward
        // scaffolding and then name-matched out of the reward and shop channels — two mechanisms for one
        // question, and a catalog that quietly held another generation's content. Generation Profile Stage 2b
        // made that question a seam (IContentScope), whose Gen 1 implementation is an identity function; that is
        // only honest if the catalog really does hold one generation's content, so the item was dropped from the
        // roster and from items.db. It returns via the per-generation item data in TODO.md -> Multi-Generation,
        // not by being re-added here — hence this guard rather than a comment.
        Assert.DoesNotContain("max-revive", ItemMapper.Gen1BattleItemNames);
        Assert.Contains("revive", ItemMapper.Gen1BattleItemNames);
    }

    [Theory]
    [InlineData("ether")]
    [InlineData("elixir")]
    public void MapToItem_PpRestore_FixedTen(string name)
    {
        var item = ItemMapper.MapToItem(PokeItem(name, "pp-recovery"));
        Assert.Equal(10, item.PpRestoreAmount);
        Assert.False(item.RestoresAllPp);
    }

    [Theory]
    [InlineData("max-ether")]
    [InlineData("max-elixir")]
    public void MapToItem_MaxPpRestore_RestoresAllPp(string name)
    {
        var item = ItemMapper.MapToItem(PokeItem(name, "pp-recovery"));
        Assert.True(item.RestoresAllPp);
        Assert.Null(item.PpRestoreAmount);
    }

    [Theory]
    [InlineData("ether", false)] // Ether / Max Ether target ONE move
    [InlineData("max-ether", false)]
    [InlineData("elixir", true)] // Elixir / Max Elixir restore EVERY move
    [InlineData("max-elixir", true)]
    public void MapToItem_PpRestore_AllMovesScope(string name, bool allMoves)
    {
        var item = ItemMapper.MapToItem(PokeItem(name, "pp-recovery"));
        Assert.Equal(allMoves, item.RestoresPpAllMoves);
    }

    [Theory]
    [InlineData("x-attack", StageStat.Attack)]
    [InlineData("x-defense", StageStat.Defense)]
    [InlineData("x-speed", StageStat.Speed)]
    [InlineData("x-sp-atk", StageStat.Special)] // modern name for Gen 1's X Special
    [InlineData("x-accuracy", StageStat.Accuracy)]
    public void MapToItem_XItems_RaiseOneStageOfOneStat(string name, StageStat stat)
    {
        var item = ItemMapper.MapToItem(PokeItem(name, "stat-boosts"));
        Assert.Equal(stat, item.StatBoostStat);
        Assert.Equal(1, item.StatBoostStages);
    }

    [Fact]
    public void MapToItem_XAccuracy_DescribesTheAccuracyBypassNotAStageBoost()
    {
        var item = ItemMapper.MapToItem(PokeItem("x-accuracy", "stat-boosts"));

        Assert.Contains("accuracy check", item.Description);
        Assert.DoesNotContain("stage", item.Description);
    }

    [Fact]
    public void MapToItem_DireHit_BoostsCrit()
    {
        // Dire Hit sets Gen 1's bugged Focus Energy state; a flag, not a stat stage.
        var item = ItemMapper.MapToItem(PokeItem("dire-hit", "stat-boosts"));
        Assert.True(item.BoostsCrit);
        Assert.False(item.SetsMist);
        Assert.Null(item.StatBoostStat);
    }

    [Fact]
    public void MapToItem_GuardSpec_SetsMist()
    {
        var item = ItemMapper.MapToItem(PokeItem("guard-spec", "stat-boosts"));
        Assert.True(item.SetsMist);
        Assert.False(item.BoostsCrit);
        Assert.Null(item.StatBoostStat);
    }
}
