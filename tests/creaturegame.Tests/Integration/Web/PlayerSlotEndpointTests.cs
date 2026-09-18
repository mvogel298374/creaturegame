using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Generations;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;
using creaturegame.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// End-to-end coverage for <c>GET /api/game/{gameId}/player/{slot}</c> (docs/TODO.md — the CHECK POKEMON
/// party-member picker) against a <b>genuine multi-member party wired into an active battle</b> — the level
/// <see cref="PartySlotResolutionTests"/>' pure <c>GameSessionManager.PartyMemberAt</c> coverage doesn't reach:
/// the actual <see cref="GameController.GetPlayerSlot"/> action, its 404 contract, and the DTO the picker
/// renders for each slot. Uses <see cref="GameSessionManager.ActivePartyFor"/> to grow the party directly
/// (<c>party.Add(...)</c>) rather than running the full acquisition flow, and
/// <see cref="TestSupport.BlockedEncounterFactory"/> to keep the spawned run task parked and DB-free, same
/// precedent as <c>SessionResumeTests</c>.
/// </summary>
public class PlayerSlotEndpointTests
{
    private static (
        GameController controller,
        GameSessionManager manager,
        ManualResetEventSlim gate
    ) Build()
    {
        var gate = new ManualResetEventSlim(initialState: false);
        var factory = BlockedEncounterFactory.Create(gate);
        var manager = new GameSessionManager(new RecordingHubContext(), factory);
        return (new GameController(manager, factory), manager, gate);
    }

    private static string StartActiveGame(GameSessionManager manager, Creature starter)
    {
        string gameId = manager.RegisterSession(
            starter,
            [],
            new Bag(),
            new Wallet(),
            [],
            new SeededRandomSource(1),
            [],
            Difficulty.Normal,
            Generation.One
        );
        Assert.True(manager.AttachConnection(gameId, "conn-1")); // claims it: pending -> active
        return gameId;
    }

    private static PlayerOverviewDto AsOk(IActionResult result) =>
        Assert.IsType<PlayerOverviewDto>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public void GetPlayerSlot_ReturnsEachMembersOwnSheet_NotJustTheLeads()
    {
        var (controller, manager, gate) = Build();
        try
        {
            var starter = TestCreatures.Make("STARTMON", hp: 100);
            string gameId = StartActiveGame(manager, starter);
            var party = manager.ActivePartyFor(gameId);
            Assert.NotNull(party);
            party!.Add(TestCreatures.Make("BENCHMON", hp: 80));

            var slot0 = AsOk(controller.GetPlayerSlot(gameId, 0));
            var slot1 = AsOk(controller.GetPlayerSlot(gameId, 1));

            Assert.Equal("STARTMON", slot0.Name);
            Assert.Equal(100, slot0.MaxHp);
            Assert.Equal("BENCHMON", slot1.Name);
            Assert.Equal(80, slot1.MaxHp);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void GetPlayerSlot_StillReturnsAFaintedBenchMember_NotJustLiveOnes()
    {
        // The whole point of the picker: CHECK POKEMON must be able to show a downed bench member's sheet, not
        // just the survivors — a plain "resolve the active/live creature" read (GetPlayer, no slot) would hide it.
        var (controller, manager, gate) = Build();
        try
        {
            var starter = TestCreatures.Make("STARTMON", hp: 100);
            string gameId = StartActiveGame(manager, starter);
            var fainted = TestCreatures.Make("FAINTEDMON", hp: 80);
            fainted.Attributes.HP = 0;
            manager.ActivePartyFor(gameId)!.Add(fainted);

            var slot1 = AsOk(controller.GetPlayerSlot(gameId, 1));

            Assert.Equal("FAINTEDMON", slot1.Name);
            Assert.Equal(0, slot1.Hp);
            Assert.Equal(80, slot1.MaxHp);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void GetPlayerSlot_MatchesTheOrdinalPositionGetPartyProjects()
    {
        // Nothing else pins that the picker's index (from the /party projection the UI renders cards from) and
        // the /player/{slot} index resolve the SAME creature. A future reorder of one without the other would
        // silently point every picker card at the wrong sheet with an otherwise-green suite.
        var (controller, manager, gate) = Build();
        try
        {
            var starter = TestCreatures.Make("STARTMON", hp: 100);
            string gameId = StartActiveGame(manager, starter);
            var party = manager.ActivePartyFor(gameId);
            party!.Add(TestCreatures.Make("BENCHMON", hp: 80));
            party.Add(TestCreatures.Make("THIRDMON", hp: 60));

            var projected = manager.GetParty(gameId);
            Assert.NotNull(projected);

            for (int i = 0; i < projected!.Count; i++)
            {
                var projectedName = (string)
                    projected[i].GetType().GetProperty("Name")!.GetValue(projected[i])!;
                Assert.Equal(projectedName, AsOk(controller.GetPlayerSlot(gameId, i)).Name);
            }
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void GetPlayerSlot_Returns404_ForASlotBeyondThePartysCurrentSize()
    {
        var (controller, manager, gate) = Build();
        try
        {
            string gameId = StartActiveGame(manager, TestCreatures.Make("STARTMON"));

            var result = controller.GetPlayerSlot(gameId, 1); // no second member added — only slot 0 exists

            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            gate.Set();
        }
    }

    [Fact]
    public void GetPlayerSlot_Returns404_ForAnUnknownGameId()
    {
        var (controller, _, gate) = Build();
        try
        {
            var result = controller.GetPlayerSlot("no-such-game", 0);

            Assert.IsType<NotFoundObjectResult>(result);
        }
        finally
        {
            gate.Set();
        }
    }
}
