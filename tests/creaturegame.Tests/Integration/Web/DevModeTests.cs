using creaturegame.Combat;
using creaturegame.Generations;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;
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
        var controller = new DevController(new DevModeOptions(enabled), NewManager(out _));

        var ok = Assert.IsType<OkObjectResult>(controller.GetStatus());

        Assert.Equal(enabled, ok.Value!.GetType().GetProperty("enabled")!.GetValue(ok.Value));
    }

    private static GameSessionManager NewManager(out ManualResetEventSlim gate)
    {
        gate = new ManualResetEventSlim(initialState: false);
        return new GameSessionManager(
            new RecordingHubContext(),
            BlockedEncounterFactory.Create(gate)
        );
    }

    private static string StartActiveGame(GameSessionManager manager)
    {
        string gameId = manager.RegisterSession(
            TestCreatures.Make("STARTMON"),
            [],
            new Bag(),
            new Wallet(),
            [],
            new SeededRandomSource(1),
            [],
            Difficulty.Normal,
            Generation.One,
            new Dictionary<int, IReadOnlyList<int>>()
        );
        Assert.True(manager.AttachConnection(gameId, "conn-1"));
        return gameId;
    }

    [Fact]
    public void EnemyEndpointIs404WhenDevModeIsOff_EvenForARealEnemy()
    {
        var manager = NewManager(out var gate);
        try
        {
            string gameId = StartActiveGame(manager);
            manager.RecordEnemy(gameId, TestCreatures.Make("FOE", hp: 77));
            var controller = new DevController(new DevModeOptions(false), manager);

            Assert.IsType<NotFoundResult>(controller.GetEnemy(gameId));
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void EnemyEndpointReturnsTheFoesSheetWhenOn_And404sWithoutAnEnemyOrGame()
    {
        var manager = NewManager(out var gate);
        try
        {
            string gameId = StartActiveGame(manager);
            var controller = new DevController(new DevModeOptions(true), manager);

            Assert.IsType<NotFoundObjectResult>(controller.GetEnemy(gameId)); // no encounter yet
            Assert.IsType<NotFoundObjectResult>(controller.GetEnemy("nope"));

            manager.RecordEnemy(gameId, TestCreatures.Make("FOE", hp: 77));
            var dto = Assert.IsType<PlayerOverviewDto>(
                Assert.IsType<OkObjectResult>(controller.GetEnemy(gameId)).Value
            );
            Assert.Equal("FOE", dto.Name);
            Assert.Equal(77, dto.MaxHp);
        }
        finally
        {
            gate.Set();
        }
    }
}
