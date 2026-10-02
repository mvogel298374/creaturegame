using creaturegame.Web.Battle;
using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Web.Controllers;

/// <summary>Dev Mode endpoints (docs/TODO.md — Dev Mode). <see cref="GetStatus"/> is the one ungated read —
/// it only reports whether the switch is on; every other action here must 404 when it is off.</summary>
[ApiController]
[Route("api/dev")]
public class DevController(DevModeOptions devMode) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(new { enabled = devMode.Enabled });
}
