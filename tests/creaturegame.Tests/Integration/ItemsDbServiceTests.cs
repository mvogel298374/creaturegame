using System.Collections.Generic;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Items;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Verifies the items.db migration applies to a fresh SQLite database and that <see cref="ItemService"/>
/// round-trips real <see cref="Item"/> rows (no mocks — drives the actual EF context).
/// </summary>
public class ItemsDbServiceTests : IDisposable
{
    private readonly string _itemsDb = Path.ChangeExtension(Path.GetTempFileName(), ".db");

    private ItemsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ItemsDbContext>()
            .UseSqlite($"Data Source={_itemsDb}")
            .Options;
        return new ItemsDbContext(options);
    }

    [Fact]
    public void EnsureDatabaseCreated_IsIdempotent()
    {
        using var context = BuildContext();
        var ex = Record.Exception(() =>
        {
            context.EnsureDatabaseCreated();
            context.EnsureDatabaseCreated();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Schema_HasAllExpectedColumns()
    {
        using var context = BuildContext();
        context.EnsureDatabaseCreated();

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection($"Data Source={_itemsDb}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Items)";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(1));

        foreach (
            var col in new[]
            {
                "Id",
                "Name",
                "Category",
                "Cost",
                "FlingPower",
                "Description",
                "SpriteUrl",
                "HealAmount",
                "HealsAllHp",
                "CuresAllStatus",
                "CuredStatus",
                "RevivePercent",
                "PpRestoreAmount",
                "RestoresAllPp",
                "RestoresPpAllMoves",
                "StatBoostStat",
                "StatBoostStages",
                "BoostsCrit",
                "SetsMist",
            }
        )
            Assert.Contains(col, columns);
    }

    [Fact]
    public async Task UpsertAndGet_RoundTripsAllFields()
    {
        using var context = BuildContext();
        context.EnsureDatabaseCreated();
        var service = new ItemService(context);

        await service.UpsertItemAsync(
            new Item
            {
                Id = 17,
                Name = "antidote",
                Category = ItemCategory.StatusCure,
                Cost = 100,
                Description = "Cures poison.",
                SpriteUrl = "https://img/antidote.png",
                CuredStatus = StatusCondition.Poison,
            }
        );

        var loaded = await service.GetItemByIdAsync(17);
        Assert.NotNull(loaded);
        Assert.Equal("antidote", loaded!.Name);
        Assert.Equal(ItemCategory.StatusCure, loaded.Category);
        Assert.Equal(StatusCondition.Poison, loaded.CuredStatus);
        Assert.Equal("https://img/antidote.png", loaded.SpriteUrl);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingRowInPlace()
    {
        using var context = BuildContext();
        context.EnsureDatabaseCreated();
        var service = new ItemService(context);

        await service.UpsertItemAsync(
            new Item
            {
                Id = 4,
                Name = "potion",
                Cost = 200,
            }
        );
        await service.UpsertItemAsync(
            new Item
            {
                Id = 4,
                Name = "potion",
                Cost = 300,
            }
        );

        Assert.Single(await service.GetAllItemsAsync());
        Assert.Equal(300, (await service.GetItemByIdAsync(4))!.Cost);
    }

    [Fact]
    public async Task GetByName_IsCaseInsensitive()
    {
        using var context = BuildContext();
        context.EnsureDatabaseCreated();
        var service = new ItemService(context);
        await service.UpsertItemAsync(new Item { Id = 1, Name = "great-ball" });

        Assert.NotNull(await service.GetItemByNameAsync("GREAT-BALL"));
    }

    [Fact]
    public async Task GetByCategory_FiltersToThatCategory()
    {
        using var context = BuildContext();
        context.EnsureDatabaseCreated();
        var service = new ItemService(context);
        await service.UpsertItemAsync(
            new Item
            {
                Id = 1,
                Name = "poke-ball",
                Category = ItemCategory.Ball,
            }
        );
        await service.UpsertItemAsync(
            new Item
            {
                Id = 2,
                Name = "great-ball",
                Category = ItemCategory.Ball,
            }
        );
        await service.UpsertItemAsync(
            new Item
            {
                Id = 3,
                Name = "potion",
                Category = ItemCategory.Healing,
            }
        );

        var balls = await service.GetItemsByCategoryAsync(ItemCategory.Ball);
        Assert.Equal(2, balls.Count);
        Assert.All(balls, i => Assert.Equal(ItemCategory.Ball, i.Category));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_itemsDb))
            File.Delete(_itemsDb);
    }
}
