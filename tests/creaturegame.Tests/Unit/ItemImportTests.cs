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
        int cost = 100,
        int? flingPower = null,
        string? shortEffect = null
    ) =>
        new()
        {
            Id = id,
            Name = name,
            Cost = cost,
            FlingPower = flingPower,
            Category = category == null ? null : new NamedApiResource { Name = category },
            Sprites = new ItemSprites { Default = $"https://img/{name}.png" },
            EffectEntries =
                shortEffect == null
                    ? null
                    :
                    [
                        new EffectEntry
                        {
                            ShortEffect = shortEffect,
                            Language = new() { Name = "en" },
                        },
                    ],
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
            PokeItem(
                "great-ball",
                "standard-balls",
                id: 3,
                cost: 600,
                flingPower: 0,
                shortEffect: "Catches."
            )
        );

        Assert.Equal(3, item.Id);
        Assert.Equal("great-ball", item.Name);
        Assert.Equal(600, item.Cost);
        Assert.Equal(0, item.FlingPower);
        Assert.Equal("Catches.", item.Description);
        Assert.Equal("https://img/great-ball.png", item.SpriteUrl);
    }

    [Fact]
    public void MapToItem_FallsBackWhenNoEnglishEffect()
    {
        var item = ItemMapper.MapToItem(PokeItem("potion", "healing", shortEffect: null));
        Assert.Equal("No description available.", item.Description);
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
        var item = ItemMapper.MapToItem(PokeItem("x", pokeApiCategory));
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

    [Theory]
    [InlineData("revive", 50)]
    [InlineData("max-revive", 100)]
    public void MapToItem_RevivePercents(string name, int percent)
    {
        var item = ItemMapper.MapToItem(PokeItem(name, "revival"));
        Assert.Equal(percent, item.RevivePercent);
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
    [InlineData("x-special", StageStat.Special)]
    [InlineData("x-sp-atk", StageStat.Special)] // modern name for Gen 1's X Special
    [InlineData("x-accuracy", StageStat.Accuracy)]
    public void MapToItem_XItems_RaiseOneStageOfOneStat(string name, StageStat stat)
    {
        var item = ItemMapper.MapToItem(PokeItem(name, "stat-boosts"));
        Assert.Equal(stat, item.StatBoostStat);
        Assert.Equal(1, item.StatBoostStages);
    }

    [Fact]
    public void MapToItem_DireHit_BoostsCrit()
    {
        // Dire Hit raises crit (Gen 1 Focus Energy state); it's a booster, not a stat-stage change.
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
