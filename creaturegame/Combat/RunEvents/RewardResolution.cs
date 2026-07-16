using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// Shared reward-choice resolution used by every reward-earning node (battle win, Treasure, Mystery): if the
/// roll produced a choice, offer it — a blocking pick-one-of-N the client raises the modal for — clamp the
/// picked index, apply the chosen option (gold → wallet, item → bag), and announce it with a
/// <see cref="RewardGranted"/> (so the HUD/log render exactly as before, now driven by the <em>chosen</em>
/// option). An empty roll (<see cref="RewardChoice.None"/>) is silent. Headless / AI inputs auto-pick option 0
/// via the <see cref="IBattleInput.ChooseRewardAsync"/> default, so a run never stalls on the modal.
/// </summary>
internal static class RewardResolution
{
    public static async Task OfferAndApplyAsync(
        RewardChoice choice,
        string source,
        Wallet? wallet,
        Bag? playerBag,
        RunContext ctx
    )
    {
        if (choice.IsEmpty)
            return;

        ctx.Emitter?.Emit(new RewardChoiceOffered(source, choice.Options));
        int index = await ctx.PlayerInput.ChooseRewardAsync(
            new RewardChoiceContext(source, choice.Options)
        );
        // Tolerate a stale / malformed pick — fall back to the first option (mirrors the biome-choice fallback),
        // so the run is never left unresolved on an out-of-range index.
        if (index < 0 || index >= choice.Options.Count)
            index = 0;

        int gold = 0;
        var itemNames = new List<string>();
        switch (choice.Options[index])
        {
            case GoldRewardOption g:
                gold = g.Gold;
                wallet?.Credit(gold);
                break;
            case ItemRewardOption item:
                playerBag?.Add(item.ItemId);
                itemNames.Add(item.ItemName);
                break;
            case HealRewardOption heal:
                ApplyHeal(heal, ctx.State.Player, ctx.Emitter);
                itemNames.Add(heal.Label);
                break;
        }

        ctx.Emitter?.Emit(new RewardGranted(source, gold, wallet?.Balance ?? gold, itemNames));
    }

    // Applies a pre-resolved quick-heal to the player's creature on the spot — but only the components the option
    // carries (the web policy set each flag from what the creature actually needed): restore some HP, cure any
    // status, and — when RestoreLowPp is set — top EVERY non-full move back to max (Elixir-style, matching
    // PpRestoreItemEffect's all-moves precedent), not only the move that tripped the low-PP threshold. Reuses the
    // same gen-invariant primitives + events as item use (Healed / StatusCleared / PpRestored), so the client's
    // timeline renders it exactly like a potion/status-cure.
    internal static void ApplyHeal(
        HealRewardOption heal,
        Creature player,
        IBattleEventEmitter? emitter
    )
    {
        if (heal.HpRestore > 0 && player.Attributes.HP < player.Attributes.MaxHP)
        {
            int before = player.Attributes.HP;
            player.Attributes.ReceiveHealing(heal.HpRestore); // caps at MaxHP
            emitter?.Emit(
                new Healed(player.Name, player.Attributes.HP - before, player.Attributes.HP)
            );
        }

        if (heal.CureStatus && player.Battle.Status != StatusCondition.None)
            HealingItemEffect.ClearStatus(player, emitter);

        if (heal.RestoreLowPp)
        {
            foreach (var move in player.MoveSet)
            {
                if (move.PowerPointsCurrent < move.Base.PowerPointsMax)
                {
                    move.PowerPointsCurrent = move.Base.PowerPointsMax;
                    emitter?.Emit(
                        new PpRestored(player.Name, move.Base.Name ?? "", move.PowerPointsCurrent)
                    );
                }
            }
        }
    }
}
