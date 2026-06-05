using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.DB;

public class PokemonService
{
    private readonly PokemonDbContext _context;

    public PokemonService(PokemonDbContext context)
    {
        _context = context;
    }

    public async Task<PokemonSpecies?> GetSpeciesByNameAsync(string name)
    {
        return await _context
            .Species.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name.ToLower() == name.ToLower());
    }

    public async Task<PokemonSpecies?> GetSpeciesByIdAsync(int id)
    {
        return await _context.Species.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<List<PokemonSpecies>> GetAllSpeciesAsync()
    {
        return await _context.Species.AsNoTracking().ToListAsync();
    }
}
