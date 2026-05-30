using creaturegame.Attacks;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class MovesDbContext : DbContext
{
    public MovesDbContext() { }
    public MovesDbContext(DbContextOptions<MovesDbContext> options) : base(options) { }

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

public class PokemonDbContext : DbContext
{
    public PokemonDbContext() { }
    public PokemonDbContext(DbContextOptions<PokemonDbContext> options) : base(options) { }

    public DbSet<PokemonSpecies> Species { get; set; }
    public DbSet<PokemonGameAvailability> GameAvailability { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            string dbPath = DbPathHelper.GetDatabasePath("pokemon.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PokemonSpecies>().ToTable("PokemonSpecies");
        modelBuilder.Entity<PokemonGameAvailability>().ToTable("PokemonGameAvailability");
        modelBuilder.Entity<PokemonGameAvailability>()
            .HasIndex(g => new { g.SpeciesId, g.GameVersion })
            .IsUnique();
    }

    public void EnsureDatabaseCreated()
    {
        Database.Migrate();
    }
}