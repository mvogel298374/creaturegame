using System.Text.Json;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
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

    // ── Damage ranges (docs/TODO.md — Dev Mode damage ranges) ──────────────────────────────────────────────

    private static Attack Strike() =>
        new()
        {
            Name = "Strike",
            BaseDamage = 60,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Normal,
        };

    [Fact]
    public async Task DamageDealtCarriesARangeThatBracketsTheActualHit()
    {
        var attacker = TestCreatures.Make("A", attack: 120);
        var defender = TestCreatures.Make("D", defense: 80, hp: 5000);
        var emitter = new RecordingEmitter();

        await new AttackAction(
            attacker,
            defender,
            new PokemonAttack(Strike()),
            new Gen1TypeChart(),
            new NoVarianceNoCritHitRules(),
            emitter
        ).ExecuteAsync();

        var hit = Assert.Single(emitter.Of<DamageDealt>());
        Assert.NotNull(hit.MinDamage);
        Assert.NotNull(hit.MaxDamage);
        Assert.InRange(hit.Damage, hit.MinDamage!.Value, hit.MaxDamage!.Value);
    }

    private static JsonElement DamageDealtPayload(RecordingHubContext hub)
    {
        var (_, payload) = Assert.Single(hub.EventsFor("conn-1"), e => e.Type == "DamageDealt");
        return JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheEmitterWithholdsTheRangeUnlessDevModeIsOn(bool devOn)
    {
        var hub = new RecordingHubContext();
        var emitter = new SignalRBattleEventEmitter(hub, () => "conn-1", includeDamageRange: devOn);

        emitter.Emit(new DamageDealt("D", 1, 30, 1.0, 70, 100, false, 25, 35));

        var payload = DamageDealtPayload(hub);
        Assert.Equal(30, payload.GetProperty("Damage").GetInt32());
        Assert.Equal(
            devOn ? 25 : (int?)null,
            payload.GetProperty("MinDamage").ValueKind == JsonValueKind.Null
                ? null
                : payload.GetProperty("MinDamage").GetInt32()
        );
        Assert.Equal(devOn, payload.GetProperty("MaxDamage").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public void DamageRangesEndpointIs404WhenOff_AndParallelsTheMovesetWhenOn()
    {
        var manager = NewManager(out var gate);
        try
        {
            var starter = TestCreatures.Make("STARTMON", attack: 100);
            var strike = Strike();
            strike.Id = 1;
            starter.AddAttack(strike);
            starter.AddAttack(
                new Attack
                {
                    Id = 2,
                    Name = "Growl",
                    BaseDamage = 0,
                }
            );
            string gameId = manager.RegisterSession(
                starter,
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
            manager.RecordEnemy(gameId, TestCreatures.Make("FOE", hp: 200));

            Assert.IsType<NotFoundResult>(
                new DevController(new DevModeOptions(false), manager).GetDamageRanges(
                    gameId,
                    null,
                    null
                )
            );

            var on = new DevController(new DevModeOptions(true), manager);
            var ok = Assert.IsType<OkObjectResult>(on.GetDamageRanges(gameId, "player", null));
            var ranges = JsonDocument
                .Parse(JsonSerializer.Serialize(ok.Value))
                .RootElement.GetProperty("ranges");
            Assert.Equal(2, ranges.GetArrayLength());
            Assert.True(
                ranges[0].GetProperty("min").GetInt32() <= ranges[0].GetProperty("max").GetInt32()
            );
            Assert.Equal(JsonValueKind.Null, ranges[1].ValueKind); // Growl: no damage to show

            Assert.IsType<NotFoundObjectResult>(on.GetDamageRanges("nope", "player", null));
        }
        finally
        {
            gate.Set();
        }
    }
}
