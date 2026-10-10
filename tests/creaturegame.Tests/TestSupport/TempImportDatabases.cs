using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Tests.TestSupport;

/// <summary>Fresh, migrated temp-file SQLite databases for driving the importer; deleted on dispose.</summary>
public sealed class TempImportDatabases : IDisposable
{
    private readonly string _moves = TempDbPath();
    private readonly string _pokemon = TempDbPath();
    private readonly string _items = TempDbPath();

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

    // Unpooled, so Dispose can delete the files without SqliteConnection.ClearAllPools(), which is process-wide.
    private static string Source(string path) => $"Data Source={path};Pooling=False";

    public TempImportDatabases()
    {
        using (var c = Moves())
            c.EnsureDatabaseCreated();
        using (var c = Pokemon())
            c.EnsureDatabaseCreated();
        using (var c = Items())
            c.EnsureDatabaseCreated();
    }

    public MovesDbContext Moves() =>
        new(new DbContextOptionsBuilder<MovesDbContext>().UseSqlite(Source(_moves)).Options);

    public PokemonDbContext Pokemon() =>
        new(new DbContextOptionsBuilder<PokemonDbContext>().UseSqlite(Source(_pokemon)).Options);

    public ItemsDbContext Items() =>
        new(new DbContextOptionsBuilder<ItemsDbContext>().UseSqlite(Source(_items)).Options);

    public void Dispose()
    {
        foreach (var path in new[] { _moves, _pokemon, _items })
            if (File.Exists(path))
                File.Delete(path);
    }
}
