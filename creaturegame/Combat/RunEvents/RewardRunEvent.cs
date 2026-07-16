using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// The Treasure/Mystery node (interaction-event): announce the node, roll its reward (guaranteed for Treasure,
/// a wildcard for Mystery — the web-layer <c>RewardCalculator</c> policy), then offer it as a pick-one-of-N
/// choice the player resolves before advancing the biome (the "open a chest" beat the client raises a modal
/// for). No reward supplier configured → the roll is <see cref="RewardChoice.None"/> and the node resolves
/// silently (an empty Mystery is itself a valid outcome).
/// </summary>
internal sealed class RewardRunEvent(
    RunNodeKind kind,
    Wallet? wallet,
    Bag? playerBag,
    Func<RewardContext, IRandomSource, RewardChoice> rewardSupplier
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        ctx.Emitter?.Emit(new RunNodeEntered(kind.ToString()));

        var choice = rewardSupplier(
            new RewardContext(
                kind,
                EnemyLevel: 0,
                ctx.State.RunDepth,
                PlayerCondition.From(ctx.State.Player)
            ),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        await RewardResolution.OfferAndApplyAsync(choice, kind.ToString(), wallet, playerBag, ctx);

        return new NodeVisitedOutcome(kind);
    }
}
