using creaturegame.Attacks;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class MovesDbContext : DbContext
{
    public MovesDbContext() { }

    public MovesDbContext(DbContextOptions<MovesDbContext> options)
        : base(options) { }

    public DbSet<Attack> Moves { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            string dbPath = DbPathHelper.GetDatabasePath("moves.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    public void EnsureDatabaseCreated()
    {
        Database.Migrate();
    }
}
