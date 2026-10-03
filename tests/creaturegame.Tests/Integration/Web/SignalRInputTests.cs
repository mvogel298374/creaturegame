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
    public async Task SetSwitchChoice_YieldsASwitchChoiceForThatPartyIndex()
    {
        // The in-battle SWITCH command (In-Combat Switching Stage B) rides the same one-per-turn handshake as
        // FIGHT/ITEM: the hub's ChooseSwitch(index) completes it, mapped to a SwitchTurnChoice the engine validates.
        var input = new SignalRInput();
        var ctx = Context(WithMoves("tackle"), TestCreatures.Make("Enemy"));

        var task = input.ChooseTurnActionAsync(ctx); // blocks on the handshake
        input.SetSwitchChoice(2); // hub: ChooseSwitch(2)
        var choice = await task;

        Assert.Equal(2, Assert.IsType<SwitchTurnChoice>(choice).PartyIndex);
    }

    [Fact]
    public async Task SetChoice_WhenOutOfPP_ResolvesToStruggle()
    {
        // Gen 1: choosing FIGHT with no usable move (all 0 PP) is Struggle — and it's driven by the FIGHT click,
        // not auto-resolved before the player chooses. The hub's ChooseMove completes the handshake; the mapper
        // yields a StruggleTurnChoice because nothing is selectable.
        var input = new SignalRInput();
        var attacker = WithMoves("tackle", "growl");
        foreach (var m in attacker.MoveSet)
            m.PowerPointsCurrent = 0; // out of PP everywhere
        var ctx = Context(attacker, TestCreatures.Make("Enemy"));

        var task = input.ChooseTurnActionAsync(ctx);
        input.SetChoice(0); // hub: ChooseMove(0) — the STRUGGLE affordance sends a move index
        var choice = await task;

        Assert.IsType<StruggleTurnChoice>(choice);
    }

    [Fact]
    public async Task SetSwitchChoice_WhenOutOfPP_IsStillHonoured()
    {
        // Gen 1 keeps the whole menu open out of PP: SWITCH (and BAG) are honoured even with no usable move — only
        // *choosing FIGHT* Struggles. The switch handshake resolves to a SwitchTurnChoice, not Struggle.
        var input = new SignalRInput();
        var attacker = WithMoves("tackle");
        foreach (var m in attacker.MoveSet)
            m.PowerPointsCurrent = 0;
        var ctx = Context(attacker, TestCreatures.Make("Enemy"));

        var task = input.ChooseTurnActionAsync(ctx);
        input.SetSwitchChoice(1); // hub: ChooseSwitch(1)
        var choice = await task;

        Assert.Equal(1, Assert.IsType<SwitchTurnChoice>(choice).PartyIndex);
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

    [Fact]
    public async Task SetBiomeChoice_YieldsTheChosenBiomeId()
    {
        var input = new SignalRInput();
        var ctx = new BiomeChoiceContext([
            new BiomeDefinition("a", "Alpha", Region.Kanto, [DamageType.Normal], []),
            new BiomeDefinition("b", "Bravo", Region.Kanto, [DamageType.Fire], []),
        ]);

        var task = input.ChooseBiomeAsync(ctx); // blocks on the map handshake
        input.SetBiomeChoice("b"); // hub: ChooseBiome("b")

        Assert.Equal("b", await task);
    }

    [Fact]
    public async Task Cancel_UnblocksThePendingBiomeChoice()
    {
        var input = new SignalRInput();
        var ctx = new BiomeChoiceContext([
            new BiomeDefinition("a", "Alpha", Region.Kanto, [DamageType.Normal], []),
        ]);

        var task = input.ChooseBiomeAsync(ctx);
        input.Cancel(); // client disconnected on the map screen

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
    }

    // ── Shop: the one iterative prompt — answers can land between two prompts ───────────────────────

    private static readonly ShopContext EmptyShop = new([], 0);

    // A dropped answer means the handshake never completes — bound the wait so that regression FAILS the test
    // (TimeoutException) instead of hanging the whole suite.
    private static Task<T> Within<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(5));

    [Fact]
    public async Task ShopActions_BuyThenLeaveArrivingBackToBack_AreBothConsumedInOrder()
    {
        // The reported soft-lock: BUY completes the pending prompt, and LEAVE arrives before the shop loop has
        // re-prompted. Both used to be handled by "complete whatever is pending", so LEAVE was dropped and the
        // loop then waited forever on a modal the client had already closed.
        var input = new SignalRInput();

        var first = input.ChooseShopActionAsync(EmptyShop);
        input.SetShopAction(new BuyShopItem(0));
        input.SetShopAction(LeaveShop.Instance); // lands before the next ChooseShopActionAsync

        Assert.Equal(new BuyShopItem(0), await Within(first));
        var second = input.ChooseShopActionAsync(EmptyShop);
        Assert.True(second.IsCompleted); // served from the backlog, not parked on a new handshake
        Assert.Same(LeaveShop.Instance, await Within(second));
    }

    [Fact]
    public async Task ShopActions_TwoQuickBuys_AreBothDelivered()
    {
        var input = new SignalRInput();

        var first = input.ChooseShopActionAsync(EmptyShop);
        input.SetShopAction(new BuyShopItem(0));
        input.SetShopAction(new BuyShopItem(1));

        Assert.Equal(new BuyShopItem(0), await Within(first));
        Assert.Equal(new BuyShopItem(1), await Within(input.ChooseShopActionAsync(EmptyShop)));
    }

    [Fact]
    public async Task ShopActions_AfterLeave_AStrayAnswerIsDroppedNotCarriedIntoTheNextShop()
    {
        var input = new SignalRInput();

        var first = input.ChooseShopActionAsync(EmptyShop);
        input.SetShopAction(LeaveShop.Instance);
        await Within(first);
        input.SetShopAction(new BuyShopItem(0)); // a click on the closing modal — the shop is over

        var nextShopsFirstPrompt = input.ChooseShopActionAsync(EmptyShop);

        Assert.False(nextShopsFirstPrompt.IsCompleted); // waits for a real answer; nothing leaked in
    }

    [Fact]
    public void ShopActions_WithNoShopOpen_AreDropped()
    {
        var input = new SignalRInput();

        input.SetShopAction(new BuyShopItem(0)); // never prompted — no shop is open

        Assert.False(input.ChooseShopActionAsync(EmptyShop).IsCompleted);
    }

    [Fact]
    public async Task ShopActions_LeaveArrivingAfterBuyWasConsumed_IsServedByTheNextPrompt()
    {
        // The branch the soft-lock fix exists for: the BUY has been fully consumed (the prompt is cleared) and the
        // shop loop hasn't re-prompted yet when LEAVE lands — no pending TCS, shop still open.
        var input = new SignalRInput();

        var first = input.ChooseShopActionAsync(EmptyShop);
        input.SetShopAction(new BuyShopItem(0));
        await Within(first);
        input.SetShopAction(LeaveShop.Instance);

        var second = input.ChooseShopActionAsync(EmptyShop);
        Assert.True(second.IsCompleted);
        Assert.Same(LeaveShop.Instance, await Within(second));
    }

    [Fact]
    public async Task ShopActions_ServingAQueuedLeave_ClearsTheRestOfTheBacklog()
    {
        var input = new SignalRInput();

        var first = input.ChooseShopActionAsync(EmptyShop);
        input.SetShopAction(new BuyShopItem(0));
        input.SetShopAction(LeaveShop.Instance);
        input.SetShopAction(new BuyShopItem(1)); // a stray click after LEAVE

        Assert.Equal(new BuyShopItem(0), await Within(first));
        Assert.Same(LeaveShop.Instance, await Within(input.ChooseShopActionAsync(EmptyShop)));
        Assert.False(input.ChooseShopActionAsync(EmptyShop).IsCompleted); // the stray BUY did not survive
    }

    [Fact]
    public async Task Cancel_WithANonEmptyShopBacklog_StillMakesTheNextShopPromptThrow()
    {
        // Pins only the cancelled check at the top of ChooseShopActionAsync (Cancel's backlog clearing is
        // housekeeping, not what makes this throw).
        var input = new SignalRInput();

        var first = input.ChooseShopActionAsync(EmptyShop);
        input.SetShopAction(new BuyShopItem(0));
        input.SetShopAction(new BuyShopItem(1)); // backlogged
        await Within(first);
        input.Cancel(); // client disconnected mid-shop

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await input.ChooseShopActionAsync(EmptyShop)
        );
    }
}
