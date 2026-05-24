using creaturegame.DB;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpeciesController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        await using var ctx = new PokemonDbContext();
        var all = await ctx.Species
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync();

        return Ok(all.Select(s => new
        {
            id           = s.Id,
            name         = s.Name,
            type1        = s.Type1.ToString(),
            type2        = s.Type2?.ToString(),
            baseHp       = s.BaseHP,
            baseAttack   = s.BaseAttack,
            baseDefense  = s.BaseDefense,
            baseSpecial  = s.BaseSpecial,
            baseSpeed    = s.BaseSpeed,
            baseStatTotal = s.BaseHP + s.BaseAttack + s.BaseDefense + s.BaseSpecial + s.BaseSpeed
        }));
    }
}
