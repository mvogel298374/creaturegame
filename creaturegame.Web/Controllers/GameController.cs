using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.DB;
using static creaturegame.Creatures.EncounterSelector;
using creaturegame.Web.Battle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace creaturegame.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController(
    GameSessionManager sessionManager,
    IDbContextFactory<PokemonDbContext> pokemonFactory,
    IDbContextFactory<MovesDbContext> movesFactory) : ControllerBase
{
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartGameRequest req)
    {
        try
        {
            await using var pokemonCtx = await pokemonFactory.CreateDbContextAsync();
            var playerSpecies = await pokemonCtx.Species.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.SpeciesId);
            if (playerSpecies == null)
                return BadRequest(new { error = "Unknown species" });

            await using var movesCtx = await movesFactory.CreateDbContextAsync();
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

            var gameId = sessionManager.RegisterSession(player, enemy, allMoves);
            return Ok(new { gameId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameController] Start failed: {ex.Message}");
            return StatusCode(500, new { error = "Failed to start game" });
        }
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
