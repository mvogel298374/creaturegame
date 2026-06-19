using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The player's turn-choice handshake in <see cref="SignalRInput"/>: the hub completes one pending choice
/// per turn with either a move (FIGHT) or a bag item (ITEM), and <see cref="SignalRInput.ChooseTurnActionAsync"/>
/// maps it to the right <see cref="TurnChoice"/>. Pure — no DB, no SignalR.
/// </summary>
public class SignalRInputTests
{
    private static TurnContext Context(Creature attacker, Creature defender) =>
        new()
        {
            Attacker = attacker,
            Defender = defender,
            TypeChart = Gen1TypeChart.Instance,
            Rules = Gen1BattleRules.Instance,
            TurnNumber = 1,
        };

    private static Creature WithMoves(params string[] names)
    {
        var c = TestCreatures.Make("Player");
        int id = 1;
        foreach (var n in names)
            c.AddAttack(
                new Attack
                {
                    Id = id++,
                    Name = n,
                    PowerPointsMax = 20,
                    BaseDamage = 40,
                }
            );
        return c;
    }

    [Fact]
    public async Task SetChoice_YieldsTheSelectedMove()
    {
        var input = new SignalRInput();
        var attacker = WithMoves("tackle", "growl");
        var ctx = Context(attacker, TestCreatures.Make("Enemy"));

        var task = input.ChooseTurnActionAsync(ctx); // blocks on the handshake
        input.SetChoice(1); // hub: ChooseMove(1)
        var choice = await task;

        var move = Assert.IsType<MoveTurnChoice>(choice);
        Assert.Equal("growl", move.Move.Base.Name);
    }

    [Fact]
    public async Task SetChoice_OutOfRange_FallsBackToFirstSelectable()
    {
        var input = new SignalRInput();
        var attacker = WithMoves("tackle", "growl");
        var ctx = Context(attacker, TestCreatures.Make("Enemy"));

        var task = input.ChooseTurnActionAsync(ctx);
        input.SetChoice(9); // invalid slot
        var choice = await task;

        Assert.Equal("tackle", Assert.IsType<MoveTurnChoice>(choice).Move.Base.Name);
    }

    [Fact]
    public async Task SetItemChoice_YieldsAnItemChoiceWithTargetSlot()
    {
        var input = new SignalRInput();
        var ctx = Context(WithMoves("tackle"), TestCreatures.Make("Enemy"));
        var ether = new Item
        {
            Id = 38,
            Name = "ether",
            Category = ItemCategory.PpRestore,
        };

        var task = input.ChooseTurnActionAsync(ctx);
        input.SetItemChoice(ether, targetMoveSlot: 0); // hub: UseItem(38, 0)
        var choice = await task;

        var item = Assert.IsType<ItemTurnChoice>(choice);
        Assert.Equal(38, item.Item.Id);
        Assert.Equal(0, item.TargetMoveSlot);
    }

    [Fact]
    public async Task ChooseMoveAsync_NeverReturnsAnItem()
    {
        // The move-only entry point (used for the interface contract) resolves a move even if it somehow
        // saw an item request — defensive, so a move-only caller can't be handed an ItemTurnChoice.
        var input = new SignalRInput();
        var attacker = WithMoves("tackle");
        var ctx = Context(attacker, TestCreatures.Make("Enemy"));

        var task = input.ChooseMoveAsync(ctx);
        input.SetChoice(0);
        var move = await task;

        Assert.Equal("tackle", move.Base.Name);
    }

    [Fact]
    public async Task Cancel_UnblocksThePendingChoice()
    {
        var input = new SignalRInput();
        var ctx = Context(WithMoves("tackle"), TestCreatures.Make("Enemy"));

        var task = input.ChooseTurnActionAsync(ctx);
        input.Cancel(); // client disconnected

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
    }

    [Fact]
    public async Task AfterCancel_NextTurnThrowsImmediately()
    {
        var input = new SignalRInput();
        var ctx = Context(WithMoves("tackle"), TestCreatures.Make("Enemy"));
        input.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await input.ChooseTurnActionAsync(ctx)
        );
    }
}
