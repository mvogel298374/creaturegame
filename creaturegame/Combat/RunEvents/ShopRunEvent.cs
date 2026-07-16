using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// The Shop node (interaction-event): announce the node, roll this visit's run-scaled stock (the web-layer
/// <c>ShopCalculator</c> policy), then run a spend-gold <em>buy loop</em> — offer the stock, and repeatedly take
/// the player's buy/leave choice, charging the <see cref="Wallet"/> and adding to the <see cref="Bag"/> on each
/// affordable purchase — until the player leaves. Unlike the one-shot reward/biome prompts, a shop is iterative
/// (buy several, then go). No stock rolled (<see cref="ShopOffer.None"/> — no supplier / empty catalog) → the
/// node resolves as a silent banner. Headless / AI inputs leave immediately via the
/// <see cref="IBattleInput.ChooseShopActionAsync"/> default, so a run never stalls on the shop.
/// </summary>
internal sealed class ShopRunEvent(
    Wallet? wallet,
    Bag? playerBag,
    Func<ShopStockContext, IRandomSource, ShopOffer> shopSupplier
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        ctx.Emitter?.Emit(new RunNodeEntered(RunNodeKind.Shop.ToString()));

        var offer = shopSupplier(
            new ShopStockContext(ctx.State.RunDepth),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        if (offer.IsEmpty)
            return new NodeVisitedOutcome(RunNodeKind.Shop); // no stock → the banner is the whole node

        int Balance() => wallet?.Balance ?? 0;
        ctx.Emitter?.Emit(new ShopOffered(offer.Items, Balance()));

        // The buy loop: keep taking buy/leave choices until the player leaves. A buy charges the wallet and adds
        // to the bag only if affordable; a stale / out-of-range / unaffordable index is a no-op (the client's
        // balance is already correct, so no event is needed), so the loop simply re-prompts.
        while (
            await ctx.PlayerInput.ChooseShopActionAsync(new ShopContext(offer.Items, Balance()))
                is BuyShopItem buy
        )
        {
            if (buy.Index < 0 || buy.Index >= offer.Items.Count)
                continue;

            var item = offer.Items[buy.Index];
            // Refuse a buy that would exceed the Gen 1 99-per-slot ceiling — check before charging, so the
            // wallet is never spent on a clamped no-op.
            if (playerBag is not null && playerBag.IsFull(item.ItemId))
                continue;
            if (wallet is null || !wallet.TrySpend(item.Price))
                continue; // can't afford (or no wallet) → nothing bought, re-prompt

            playerBag?.Add(item.ItemId);
            ctx.Emitter?.Emit(new ShopItemPurchased(item.ItemName, item.Price, wallet.Balance));
        }

        return new NodeVisitedOutcome(RunNodeKind.Shop);
    }
}
