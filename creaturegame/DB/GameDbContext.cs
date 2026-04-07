using creaturegame.Attacks;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class GameDbContext: DbContext
{
    public DbSet<Attack> Moves { get; set; }
    public DbSet<PokemonSpecies> Species { get; set; }
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // The connection string points to a local SQLite file (moves.db in the project folder).
        optionsBuilder.UseSqlite("Data Source=moves.db");
    }

    // Optionally, override OnModelCreating to configure the model further.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // For example, you can add constraints, indexes, etc.
    }

    public void EnsureDatabaseCreated()
    {
        Database.EnsureCreated();
    }
}