using System.Text.Json;
using creaturegame.Combat;
using creaturegame.DB;
using creaturegame.Generations;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;
using creaturegame.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// docs/TODO.md — Creature Naming, Stage A: the actual wiring line inside <see cref="GameController.Start"/>
/// (<c>setup.Player.Name = NicknameRules.Normalize(req.Nickname, setup.Player.Name);</c>), beyond the pure
/// <c>NicknameRulesTests</c> coverage of the helper itself. Flagged by <c>requirements-review</c> (2026-09-14)
/// as the one DoR #6 promise the Stage A write-up hadn't actually covered — the manual Puppeteer pass proved the
/// behavior once, live, but wasn't part of the regression suite. Runs against the live <c>pokemon.db</c> /
/// <c>moves.db</c> / <c>items.db</c>, like the sibling <c>EncounterFactory</c>/<c>SpeciesController</c> probes.
/// </summary>
public class GameControllerNicknameTests
{
    private const int Bulbasaur = 1;

    private static (GameController controller, GameSessionManager manager) Build()
    {
        var factory = new EncounterFactory(
            new LiveDbContextFactory<PokemonDbContext>(() => new PokemonDbContext()),
            new LiveDbContextFactory<MovesDbContext>(() => new MovesDbContext()),
            new LiveDbContextFactory<ItemsDbContext>(() => new ItemsDbContext())
        );
        // RegisterSession never touches the hub (GenerationProfileTests' GetGeneration tests rely on the same
        // fact), and Start only registers — no connection is ever attached here.
        var manager = new GameSessionManager(hubContext: null!, factory);
        return (new GameController(manager, factory), manager);
    }

    private static async Task<string> StartAndGetGameId(
        GameController controller,
        StartGameRequest req
    )
    {
        var result = await controller.Start(req);
        var ok = Assert.IsType<OkObjectResult>(result);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        return doc.RootElement.GetProperty("gameId").GetString()!;
    }

    [Fact]
    public async Task Start_AppliesASuppliedNickname_ToThePlayerCreaturesName()
    {
        var (controller, manager) = Build();

        string gameId = await StartAndGetGameId(
            controller,
            new StartGameRequest(SpeciesId: Bulbasaur, Nickname: "sparky")
        );

        var player = manager.GetPlayerCreature(gameId);
        Assert.NotNull(player);
        Assert.Equal("SPARKY", player!.Name); // uppercased — Gen 1's real nickname-entry keyboard
        Assert.Equal("BULBASAUR", player.SpeciesName); // the species identity is untouched
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_FallsBackToTheSpeciesDefaultName_OnANullBlankOrWhitespaceNickname(
        string? nickname
    )
    {
        var (controller, manager) = Build();

        string gameId = await StartAndGetGameId(
            controller,
            new StartGameRequest(SpeciesId: Bulbasaur, Nickname: nickname)
        );

        var player = manager.GetPlayerCreature(gameId);
        Assert.NotNull(player);
        Assert.Equal("BULBASAUR", player!.Name);
        Assert.Equal("BULBASAUR", player.SpeciesName);
    }
}
