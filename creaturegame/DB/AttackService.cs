using creaturegame.Attacks;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class AttackService
{
    private readonly MovesDbContext _context;

    public AttackService(MovesDbContext context)
    {
        _context = context;
    }

    public async Task UpsertAttackAsync(Attack attack)
    {
        var existing = await _context.Moves.FindAsync(attack.Id);
        if (existing == null)
        {
            _context.Moves.Add(attack);
        }
        else
        {
            _context.Entry(existing).CurrentValues.SetValues(attack);
        }
        await _context.SaveChangesAsync();
    }

    public async Task<Attack?> GetAttackByIdAsync(int id)
    {
        return await _context.Moves.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<List<Attack>> GetAllAttacksAsync()
    {
        return await _context.Moves.AsNoTracking().ToListAsync();
    }

    public async Task<Attack?> GetAttackByNameAsync(string name)
    {
        return await _context
            .Moves.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Name != null && m.Name.ToLower() == name.ToLower());
    }
}
