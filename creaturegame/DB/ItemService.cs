using creaturegame.Items;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

/// <summary>Read/upsert API over <c>items.db</c>, parallel to <see cref="AttackService"/>. All reads use
/// <c>AsNoTracking()</c>.</summary>
public class ItemService
{
    private readonly ItemsDbContext _context;

    public ItemService(ItemsDbContext context)
    {
        _context = context;
    }

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

    public async Task<Item?> GetItemByIdAsync(int id)
    {
        return await _context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
    }

    public async Task<Item?> GetItemByNameAsync(string name)
    {
        return await _context
            .Items.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Name != null && i.Name.ToLower() == name.ToLower());
    }

    public async Task<List<Item>> GetAllItemsAsync()
    {
        return await _context.Items.AsNoTracking().ToListAsync();
    }

    public async Task<List<Item>> GetItemsByCategoryAsync(ItemCategory category)
    {
        return await _context.Items.AsNoTracking().Where(i => i.Category == category).ToListAsync();
    }
}
