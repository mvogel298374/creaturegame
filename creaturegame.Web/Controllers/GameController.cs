using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameController : ControllerBase
{
    [HttpPost("start")]
    public IActionResult Start([FromBody] StartGameRequest req)
    {
        // Phase 5: create a GameSession keyed to the SignalR connectionId
        return Ok(new { status = "started", speciesId = req.SpeciesId });
    }
}

public record StartGameRequest(int SpeciesId);
