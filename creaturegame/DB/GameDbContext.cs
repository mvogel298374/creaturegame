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
        try
        {
            Database.ExecuteSqlRaw("SELECT Priority FROM Moves LIMIT 1;");
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            Database.ExecuteSqlRaw("ALTER TABLE Moves ADD COLUMN Priority INTEGER NOT NULL DEFAULT 0;");
            Database.ExecuteSqlRaw("ALTER TABLE Moves ADD COLUMN EffectChance INTEGER;");
        }
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
            // Check for CatchRate as a proxy for the new columns
            Database.ExecuteSqlRaw("SELECT CatchRate FROM PokemonSpecies LIMIT 1;");
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            Database.ExecuteSqlRaw("ALTER TABLE PokemonSpecies ADD COLUMN CatchRate INTEGER NOT NULL DEFAULT 0;");
            Database.ExecuteSqlRaw("ALTER TABLE PokemonSpecies ADD COLUMN BaseExperience INTEGER NOT NULL DEFAULT 0;");
            Database.ExecuteSqlRaw("ALTER TABLE PokemonSpecies ADD COLUMN PokedexEntry TEXT;");
        }
        
        try
        {
            // Simple check to see if GrowthRate column exists (added in previous step)
            Database.ExecuteSqlRaw("SELECT GrowthRate FROM PokemonSpecies LIMIT 1;");
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // If it doesn't, add it
            Database.ExecuteSqlRaw("ALTER TABLE PokemonSpecies ADD COLUMN GrowthRate INTEGER NOT NULL DEFAULT 1;");
        }
    }
}