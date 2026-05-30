using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Web.Battle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController(GameSessionManager sessionManager) : ControllerBase
{
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartGameRequest req)
    {
        try
        {
            await using var pokemonCtx = new PokemonDbContext();
            var playerSpecies = await pokemonCtx.Species.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SpeciesId);
            if (playerSpecies == null)
                return BadRequest(new { error = "Unknown species" });

            await using var movesCtx = new MovesDbContext();
            var allMoves = await movesCtx.Moves.AsNoTracking().ToListAsync();
            if (allMoves.Count == 0)
                return StatusCode(500, new { error = "No moves in database" });

            int playerLevel = Math.Clamp(req.Level ?? 50, 5, 100);
            int playerBst   = Bst(playerSpecies);

            var allSpecies  = await pokemonCtx.Species.AsNoTracking()
                                  .Where(s => s.Id != playerSpecies.Id)
                                  .ToListAsync();
            var enemySpecies = PickByBst(allSpecies, playerBst);
            if (enemySpecies == null)
                return StatusCode(500, new { error = "No species in database" });

            int enemyLevel = Math.Clamp(playerLevel + Random.Shared.Next(-3, 4), 5, 100);

            var player = BuildCreature(playerSpecies, allMoves, playerLevel);
            var enemy  = BuildCreature(enemySpecies,  allMoves, enemyLevel);

            var gameId = sessionManager.RegisterSession(player, enemy);
            return Ok(new { gameId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameController] Start failed: {ex.Message}");
            return StatusCode(500, new { error = "Failed to start game" });
        }
    }

    private static int Bst(PokemonSpecies s) =>
        s.BaseHP + s.BaseAttack + s.BaseDefense + s.BaseSpecial + s.BaseSpeed;

    private static PokemonSpecies? PickByBst(List<PokemonSpecies> pool, int playerBst)
    {
        foreach (var pct in new[] { 0.15, 0.25, 0.50, 1.0 })
        {
            int lo = (int)(playerBst * (1.0 - pct));
            int hi = (int)(playerBst * (1.0 + pct));
            var candidates = pool.Where(s => Bst(s) >= lo && Bst(s) <= hi).ToList();
            if (candidates.Count > 0)
                return candidates[Random.Shared.Next(candidates.Count)];
        }
        return pool.Count > 0 ? pool[Random.Shared.Next(pool.Count)] : null;
    }

    private static Creature BuildCreature(PokemonSpecies species, List<Attack> allMoves, int level = 50)
    {
        var creature = new Creature(species.Name.ToUpper()) { Level = level };
        creature.InitializeFromSpecies(species);
        creature.Experience = creature.CalculateExperienceForLevel(level);
        foreach (var move in allMoves.OrderBy(_ => Random.Shared.Next()).Take(4))
            creature.AddAttack(move);
        return creature;
    }
}

public record StartGameRequest(int SpeciesId, int? Level = null);
