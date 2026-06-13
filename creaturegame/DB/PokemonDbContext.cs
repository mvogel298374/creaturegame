using creaturegame.Attacks;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class PokemonDbContext : DbContext
{
    public PokemonDbContext() { }

    public PokemonDbContext(DbContextOptions<PokemonDbContext> options)
        : base(options) { }

    public DbSet<PokemonSpecies> Species { get; set; }
    public DbSet<PokemonGameAvailability> GameAvailability { get; set; }
    public DbSet<PokemonLearnset> Learnsets { get; set; }

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
        modelBuilder
            .Entity<PokemonGameAvailability>()
            .HasIndex(g => new { g.SpeciesId, g.GameVersion })
            .IsUnique();

        modelBuilder.Entity<PokemonLearnset>().ToTable("PokemonLearnset");
        modelBuilder
            .Entity<PokemonLearnset>()
            .HasOne<PokemonSpecies>()
            .WithMany()
            .HasForeignKey(l => l.SpeciesId)
            .OnDelete(DeleteBehavior.Cascade);
        // Drives the runtime lookup: learnset for a species in the active generation, by level.
        modelBuilder
            .Entity<PokemonLearnset>()
            .HasIndex(l => new
            {
                l.SpeciesId,
                l.Generation,
                l.LearnLevel,
            });
    }

    public void EnsureDatabaseCreated()
    {
        Database.Migrate();
    }
}
