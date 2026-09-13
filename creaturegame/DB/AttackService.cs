using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
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

    // NB: GetRandomAttackAsync/GiveDefaultMoveAsync/GiveRandomMoveAsync below have no callers anywhere in the
    // repo (incl. tests) — pre-date LearnsetMoveSelector-based move assignment. Left in place (comment-only
    // pass; flagged in TODO.md rather than deleted here).

    public async Task<Attack?> GetRandomAttackAsync(IRandomSource? rng = null)
    {
        int count = await _context.Moves.CountAsync();
        if (count == 0)
            return null;

        int index = (rng ?? SystemRandomSource.Instance).Next(count);
        return await _context.Moves.AsNoTracking().Skip(index).FirstOrDefaultAsync();
    }

    public async Task<bool> GiveDefaultMoveAsync(Creature creature)
    {
        var move = await GetAttackByNameAsync("tackle");
        return move != null && creature.AddAttack(move);
    }

    public async Task<bool> GiveRandomMoveAsync(Creature creature, IRandomSource? rng = null)
    {
        var move = await GetRandomAttackAsync(rng);
        return move != null && creature.AddAttack(move);
    }
}
