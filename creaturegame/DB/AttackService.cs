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

    /// <summary>
    /// Adds a new attack to the database or updates it if it already exists by ID.
    /// </summary>
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

    /// <summary>
    /// Retrieves an attack by its ID.
    /// </summary>
    public async Task<Attack?> GetAttackByIdAsync(int id)
    {
        return await _context.Moves.FindAsync(id);
    }

    /// <summary>
    /// Retrieves all attacks from the database.
    /// </summary>
    public async Task<List<Attack>> GetAllAttacksAsync()
    {
        return await _context.Moves.ToListAsync();
    }
    /// <summary>
    /// Retrieves an attack by its name (case-insensitive).
    /// </summary>
    public async Task<Attack?> GetAttackByNameAsync(string name)
    {
        return await _context.Moves.FirstOrDefaultAsync(m => m.Name != null && m.Name.ToLower() == name.ToLower());
    }

    /// <summary>
    /// Retrieves a random attack from the database.
    /// </summary>
    public async Task<Attack?> GetRandomAttackAsync()
    {
        int count = await _context.Moves.CountAsync();
        if (count == 0) return null;

        int index = Random.Shared.Next(count);
        return await _context.Moves.Skip(index).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Assigns a default move (tackle) to a creature if available in the database.
    /// </summary>
    public async Task<bool> GiveDefaultMoveAsync(Creature.Creature creature)
    {
        var move = await GetAttackByNameAsync("tackle");
        return move != null && creature.AddAttack(move);
    }

    /// <summary>
    /// Assigns a random move from the database to a creature.
    /// </summary>
    public async Task<bool> GiveRandomMoveAsync(Creature.Creature creature)
    {
        var move = await GetRandomAttackAsync();
        return move != null && creature.AddAttack(move);
    }
}
