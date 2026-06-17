using creaturegame.Combat;
using creaturegame.Web.Battle;
using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController(GameSessionManager sessionManager, EncounterFactory encounters)
    : ControllerBase
{
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartGameRequest req)
    {
        try
        {
            int playerLevel = Math.Clamp(req.Level ?? 50, 5, 100);

            // One seed per run. The client may supply one (replay / deterministic E2E); otherwise we pick a
            // random one. Either way the whole run — player DVs/moves, every enemy's species/level/DVs/moves,
            // the battle rolls, and the AI's choices — flows from this single seeded source, so the run is
            // reproducible by its seed. Random.Shared here only *chooses* a seed; no run draw is unseeded.
            int seed = req.Seed ?? Random.Shared.Next();
            var rng = new SeededRandomSource(seed);

            var setup = await encounters.CreatePlayerSetupAsync(req.SpeciesId, playerLevel, rng);
            if (setup == null)
                return BadRequest(new { error = "Unknown species or empty move database" });

            // The session holds the persistent player, the shared move pool, and the run's seeded RNG; each
            // encounter's enemy is built by the run loop via the EncounterFactory (so the chain can keep
            // producing foes), all drawing from the same stream.
            var gameId = sessionManager.RegisterSession(setup.Player, setup.AllMoves, rng, seed);
            Console.WriteLine($"[GameController] Started run {gameId} with seed {seed}.");
            return Ok(new { gameId, seed });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameController] Start failed: {ex.Message}");
            return StatusCode(500, new { error = "Failed to start game" });
        }
    }
}

public record StartGameRequest(int SpeciesId, int? Level = null, int? Seed = null);
