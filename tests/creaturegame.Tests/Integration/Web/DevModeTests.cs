using creaturegame.Web.Battle;
using creaturegame.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Tests.Integration.Web;

/// <summary>Dev Mode's server-side gate (docs/TODO.md — Dev Mode): the flag's resolution rule and the status
/// endpoint the client toggle keys off.</summary>
public class DevModeTests
{
    [Theory]
    [InlineData(null, true, true)] // no config: follows the environment — Development is on
    [InlineData(null, false, false)] // ...Production is off
    [InlineData(true, false, true)] // explicit config wins over the environment, either direction
    [InlineData(false, true, false)]
    public void ResolveFollowsConfigThenEnvironment(
        bool? configured,
        bool isDevelopment,
        bool expected
    ) => Assert.Equal(expected, DevModeOptions.Resolve(configured, isDevelopment).Enabled);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StatusEndpointReportsTheServerFlag(bool enabled)
    {
        var controller = new DevController(new DevModeOptions(enabled));

        var ok = Assert.IsType<OkObjectResult>(controller.GetStatus());

        Assert.Equal(enabled, ok.Value!.GetType().GetProperty("enabled")!.GetValue(ok.Value));
    }
}
