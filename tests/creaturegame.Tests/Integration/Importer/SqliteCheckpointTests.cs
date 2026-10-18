using Microsoft.Data.Sqlite;
using PokeApiConnector;

namespace creaturegame.Tests.Integration.Importer;

public class SqliteCheckpointTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
    private readonly string _copy = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _db, $"{_db}-wal", $"{_db}-shm", _copy })
            File.Delete(path);
    }

    private void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void WriteThroughAPooledConnection()
    {
        using var writer = new SqliteConnection($"Data Source={_db}");
        writer.Open();
        Execute(writer, "PRAGMA journal_mode=WAL;");
        Execute(writer, "CREATE TABLE Item (Id INTEGER PRIMARY KEY);");
        Execute(writer, "INSERT INTO Item VALUES (17);");
    }

    [Fact]
    public void FlushAll_SkipsADatabaseThatDoesNotExist()
    {
        Assert.True(SqliteCheckpoint.FlushAll([_copy]));
        Assert.False(File.Exists(_copy));
    }

    [Fact]
    public void Flush_OnAMissingPath_ThrowsInsteadOfCreatingAnEmptyDatabase()
    {
        Assert.Throws<SqliteException>(() => SqliteCheckpoint.Flush(_copy));
        Assert.False(File.Exists(_copy));
    }

    [Fact]
    public void FlushAll_ReportsFailure_WhenAnOpenReadTransactionBlocksTheCheckpoint()
    {
        WriteThroughAPooledConnection();
        using var holder = new SqliteConnection($"Data Source={_db};Pooling=False");
        holder.Open();
        Execute(holder, "BEGIN;");
        Execute(holder, "SELECT COUNT(*) FROM Item;");

        Assert.False(SqliteCheckpoint.FlushAll([_db]));
    }

    [Fact]
    public void Flush_FoldsAPooledWriteIntoTheDbFile_SoACopyOfTheDbAloneHasIt()
    {
        WriteThroughAPooledConnection();
        Assert.True(new FileInfo($"{_db}-wal").Length > 0);

        SqliteCheckpoint.Flush(_db);

        Assert.Equal(0, new FileInfo($"{_db}-wal").Length);
        SqliteConnection.ClearAllPools();
        File.Copy(_db, _copy);
        using var reader = new SqliteConnection($"Data Source={_copy};Pooling=False");
        reader.Open();
        using var count = reader.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Item WHERE Id = 17;";
        Assert.Equal(1L, count.ExecuteScalar());
    }
}
