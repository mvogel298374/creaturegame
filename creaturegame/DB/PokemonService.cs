using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class PokemonService
{
    private readonly GameDbContext _context;

    public PokemonService(GameDbContext context)
    {
        _context = context;
    }

    public async Task<PokemonSpecies?> GetSpeciesByNameAsync(string name)
    {
        return await _context.Species.FirstOrDefaultAsync(s => s.Name.ToLower() == name.ToLower());
    }

    public async Task<PokemonSpecies?> GetSpeciesByIdAsync(int id)
    {
        return await _context.Species.FindAsync(id);
    }

    public async Task<List<PokemonSpecies>> GetAllSpeciesAsync()
    {
        return await _context.Species.ToListAsync();
    }
}
