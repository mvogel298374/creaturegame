using creaturegame.Items;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class ItemsDbContext : DbContext
{
    public ItemsDbContext() { }

    public ItemsDbContext(DbContextOptions<ItemsDbContext> options)
        : base(options) { }

    public DbSet<Item> Items { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            string dbPath = DbPathHelper.GetDatabasePath("items.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Item>().ToTable("Items");
    }

    public void EnsureDatabaseCreated()
    {
        Database.Migrate();
    }
}
