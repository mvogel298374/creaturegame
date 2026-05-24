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
            s.Id,
            s.Name,
            Type1 = s.Type1.ToString(),
            Type2 = s.Type2?.ToString(),
            s.BaseHP,
            s.BaseAttack,
            s.BaseDefense,
            s.BaseSpecial,
            s.BaseSpeed,
            BaseStatTotal = s.BaseHP + s.BaseAttack + s.BaseDefense + s.BaseSpecial + s.BaseSpeed
        }));
    }
}
