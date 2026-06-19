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

            // The session holds the persistent player, the shared move pool, the run's bag + item catalog, and
            // the run's seeded RNG; each encounter's enemy is built by the run loop via the EncounterFactory
            // (so the chain can keep producing foes), all drawing from the same stream.
            var gameId = sessionManager.RegisterSession(
                setup.Player,
                setup.AllMoves,
                setup.Bag,
                setup.AllItems,
                rng,
                seed
            );
            Console.WriteLine($"[GameController] Started run {gameId} with seed {seed}.");
            return Ok(new { gameId, seed });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GameController] Start failed: {ex.Message}");
            return StatusCode(500, new { error = "Failed to start game" });
        }
    }

    /// <summary>
    /// On-demand snapshot of the run's live player creature for the in-battle overview (CHECK POKEMON):
    /// actual stats, DVs, Stat-Exp, XP, and full move data. Reads the in-session <see cref="creaturegame.Creatures.Creature"/>.
    /// </summary>
    [HttpGet("{gameId}/player")]
    public IActionResult GetPlayer(string gameId)
    {
        var player = sessionManager.GetPlayerCreature(gameId);
        if (player is null)
            return NotFound(new { error = "No active game with that id" });
        return Ok(PlayerOverviewDto.From(player));
    }

    /// <summary>
    /// The run's current bag contents (held quantity joined with item data) for the in-battle bag menu.
    /// Reads the live session bag; 404 if the game is unknown or not yet started.
    /// </summary>
    [HttpGet("{gameId}/bag")]
    public IActionResult GetBag(string gameId)
    {
        var bag = sessionManager.GetBagContents(gameId);
        if (bag is null)
            return NotFound(new { error = "No active game with that id" });
        return Ok(bag);
    }
}

public record StartGameRequest(int SpeciesId, int? Level = null, int? Seed = null);
