using creaturegame.DB;
using Microsoft.Data.Sqlite;

namespace PokeApiConnector;

// DATA_IMPORT.md §5.6 → WAL checkpoint on exit.
public static class SqliteCheckpoint
{
    private static readonly string[] Databases = ["pokemon.db", "moves.db", "items.db"];

    public static bool FlushAll() => FlushAll(Databases.Select(DbPathHelper.GetDatabasePath));

    /// <summary>Checkpoints every existing database; false if any failed (reported).</summary>
    public static bool FlushAll(IEnumerable<string> paths)
    {
        bool ok = true;
        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;
            try
            {
                Flush(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WAL checkpoint FAILED for {path}: {ex.Message}");
                ok = false;
            }
        }
        return ok;
    }

    public static void Flush(string dbPath)
    {
        using var connection = new SqliteConnection(
            $"Data Source={dbPath};Mode=ReadWrite;Pooling=False"
        );
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        using var reader = command.ExecuteReader();
        reader.Read();
        if (reader.GetInt32(0) != 0)
            throw new InvalidOperationException("the checkpoint was blocked by another connection");
    }
}
