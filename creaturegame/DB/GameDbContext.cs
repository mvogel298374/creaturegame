using creaturegame.Attacks;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class MovesDbContext : DbContext
{
    public DbSet<Attack> Moves { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string dbPath = DbPathHelper.GetDatabasePath("moves.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    public void EnsureDatabaseCreated()
    {
        Database.EnsureCreated();
    }
}

public class PokemonDbContext : DbContext
{
    public DbSet<PokemonSpecies> Species { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        string dbPath = DbPathHelper.GetDatabasePath("pokemon.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PokemonSpecies>().ToTable("PokemonSpecies");
    }

    public void EnsureDatabaseCreated()
    {
        Database.EnsureCreated();
        try
        {
            // Simple check to see if GrowthRate column exists
            Database.ExecuteSqlRaw("SELECT GrowthRate FROM PokemonSpecies LIMIT 1;");
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // If it doesn't, add it
            Database.ExecuteSqlRaw("ALTER TABLE PokemonSpecies ADD COLUMN GrowthRate INTEGER NOT NULL DEFAULT 1;");
        }
    }
}