using creaturegame.Items;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

/// <summary>
/// Read API over <c>items.db</c>, parallel to <see cref="AttackService"/>. All reads use
/// <c>AsNoTracking()</c>. The bag / use-in-battle layer is not built yet — this only surfaces
/// the imported Gen 1 item data.
/// </summary>
public class ItemService
{
    private readonly ItemsDbContext _context;

    public ItemService(ItemsDbContext context)
    {
        _context = context;
    }

    /// <summary>Adds a new item or updates it if one with the same Id already exists.</summary>
    public async Task UpsertItemAsync(Item item)
    {
        var existing = await _context.Items.FindAsync(item.Id);
        if (existing == null)
        {
            _context.Items.Add(item);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(item);
        }
        await _context.SaveChangesAsync();
    }

    /// <summary>Retrieves an item by its Id.</summary>
    public async Task<Item?> GetItemByIdAsync(int id)
    {
        return await _context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
    }

    /// <summary>Retrieves an item by its name (case-insensitive).</summary>
    public async Task<Item?> GetItemByNameAsync(string name)
    {
        return await _context
            .Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Name != null && i.Name.ToLower() == name.ToLower());
    }

    /// <summary>Retrieves all items.</summary>
    public async Task<List<Item>> GetAllItemsAsync()
    {
        return await _context.Items.AsNoTracking().ToListAsync();
    }

    /// <summary>Retrieves all items in a category.</summary>
    public async Task<List<Item>> GetItemsByCategoryAsync(ItemCategory category)
    {
        return await _context.Items.AsNoTracking().Where(i => i.Category == category).ToListAsync();
    }
}
