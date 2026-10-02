using creaturegame.Web.Battle;
using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Web.Controllers;

/// <summary>Dev Mode endpoints (docs/TODO.md — Dev Mode). <see cref="GetStatus"/> is the one ungated read —
/// it only reports whether the switch is on; every other action here must 404 when it is off.</summary>
[ApiController]
[Route("api/dev")]
public class DevController(DevModeOptions devMode, GameSessionManager sessionManager)
    : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(new { enabled = devMode.Enabled });

    /// <summary>The current foe, flattened like CHECK POKEMON's <see cref="PlayerOverviewDto"/>. 404 when Dev
    /// Mode is off (indistinguishable from a missing route, so production never advertises it) or when the game
    /// is unknown / has no enemy yet.</summary>
    [HttpGet("{gameId}/enemy")]
    public IActionResult GetEnemy(string gameId)
    {
        if (!devMode.Enabled)
            return NotFound();
        var enemy = sessionManager.GetEnemyCreature(gameId);
        var generation = sessionManager.GetGeneration(gameId);
        if (enemy is null || generation is null)
            return NotFound(new { error = "No active game with that id, or no enemy yet" });
        return Ok(PlayerOverviewDto.From(enemy, generation.Value));
    }
}
