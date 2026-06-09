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
            var setup = await encounters.CreatePlayerSetupAsync(req.SpeciesId, playerLevel);
            if (setup == null)
                return BadRequest(new { error = "Unknown species or empty move database" });

            // The session holds only the persistent player + the shared move pool; each encounter's enemy
            // is built by the run loop via the EncounterFactory (so the chain can keep producing foes).
            var gameId = sessionManager.RegisterSession(setup.Player, setup.AllMoves);
            return Ok(new { gameId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameController] Start failed: {ex.Message}");
            return StatusCode(500, new { error = "Failed to start game" });
        }
    }
}

public record StartGameRequest(int SpeciesId, int? Level = null);
