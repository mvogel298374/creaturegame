using creaturegame.DB;
using creaturegame.Generations;
using creaturegame.Web.Battle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpeciesController(IDbContextFactory<PokemonDbContext> pokemonFactory) : ControllerBase
{
    /// <summary>
    /// The dex the starter picker offers — <b>the generation's</b> species, not the whole table. The client
    /// names the generation it is browsing for (`?generation=`); a missing/unrecognised value falls back to
    /// Gen 1 via the same boundary parse as game start, so a stale client keeps seeing today's full Gen 1 dex.
    /// </summary>
    /// <remarks>
    /// This was the one species read Stage 2b could not scope — it answers <i>before</i> a run exists, so there
    /// was no profile to ask. Stage 3 closes that by having the request name the generation instead: the picker
    /// is now server-authoritative (the roster of offerable starters is decided here, by the profile's content
    /// scope, exactly like the run-start starter lookup that validates the eventual pick). See
    /// <c>docs/GENERATION_PROFILE.md</c> §6.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? generation = null)
    {
        // The call boundary owns the try/catch (CLAUDE.md → Coding Conventions, amended 2026-07-20) — same
        // shape as GameController.Start: log here, return a controlled 500, keep the DB read itself thin.
        try
        {
            var profile = GameSessionManager.ProfileFor(GameController.ParseGeneration(generation));
            return Ok(await DexForAsync(profile));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SpeciesController] GetAll failed: {ex.Message}");
            return StatusCode(500, new { error = "Failed to load species" });
        }
    }

    /// <summary>
    /// The picker's dex under <paramref name="profile"/>: its content scope over the species table, in id order.
    /// <c>internal</c> so the falsification probe drives the real query against a second profile — under Gen 1
    /// the scope is an identity, so only an alternate scope can observe that this read asks it at all
    /// (<c>docs/GENERATION_PROFILE.md</c> §4.2).
    /// </summary>
    internal async Task<IReadOnlyList<SpeciesSummaryDto>> DexForAsync(GenerationProfile profile)
    {
        await using var ctx = await pokemonFactory.CreateDbContextAsync();
        var all = await profile
            .ContentScope.Species(ctx.Species.AsNoTracking())
            .OrderBy(s => s.Id)
            .ToListAsync();
        return all.Select(SpeciesSummaryDto.From).ToList();
    }
}

/// <summary>
/// The starter picker's per-species card data. Serialises to the same camelCase JSON shape the endpoint has
/// always returned (previously an anonymous object; a named type so the scoped-dex probe can read it back).
/// </summary>
public sealed record SpeciesSummaryDto(
    int Id,
    string Name,
    string Type1,
    string? Type2,
    int BaseHp,
    int BaseAttack,
    int BaseDefense,
    int BaseSpecial,
    int BaseSpeed,
    int BaseStatTotal
)
{
    public static SpeciesSummaryDto From(PokemonSpecies s) =>
        new(
            s.Id,
            s.Name,
            s.Type1.ToString(),
            s.Type2?.ToString(),
            s.BaseHP,
            s.BaseAttack,
            s.BaseDefense,
            s.BaseSpecial,
            s.BaseSpeed,
            s.BaseHP + s.BaseAttack + s.BaseDefense + s.BaseSpecial + s.BaseSpeed
        );
}
