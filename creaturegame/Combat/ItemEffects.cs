using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// Everything an item effect needs to act on the creature the item is used on, without reaching into
/// <see cref="ItemAction"/>. Effects read the item's (Gen-1) data, mutate the user's battle/permanent
/// state, and emit their own events. In-battle items are self-targeting in this scope (heal, cure, PP,
/// X-item), so there is no foe here.
/// </summary>
public sealed class ItemEffectContext
{
    public required Creature User { get; init; }
    public required Item Item { get; init; }

    /// <summary>The move slot (0–3) a single-move PP restore targets (Ether). Null for whole-moveset/non-PP items.</summary>
    public int? TargetMoveSlot { get; init; }

    public IBattleEventEmitter? Emitter { get; init; }
}

/// <summary>
/// An item's in-battle effect — the item analogue of <see cref="IMoveEffect"/>, keyed by
/// <see cref="ItemCategory"/> and resolved through <see cref="ItemEffects.For"/>. <see cref="CanApply"/>
/// decides whether the item would do anything (so <see cref="ItemAction"/> can refuse a no-effect use
/// without announcing or consuming it — the Gen 1 "won't have any effect" rule); <see cref="Apply"/>
/// performs it and emits the result events. Adding an item effect is a new class in
/// <see cref="ItemEffects.All"/>, not a switch arm.
/// </summary>
public interface IItemEffect
{
    ItemCategory Category { get; }

    /// <summary>True when using the item here would have an effect (gates the announce + consume).</summary>
    bool CanApply(ItemEffectContext ctx);

    /// <summary>Perform the effect. Only called when <see cref="CanApply"/> is true.</summary>
    void Apply(ItemEffectContext ctx);
}

/// <summary>Potion line / Max Potion / Full Restore — restore HP (Full Restore also cures status).</summary>
public sealed class HealingItemEffect : IItemEffect
{
    public ItemCategory Category => ItemCategory.Healing;

    public bool CanApply(ItemEffectContext ctx) =>
        ctx.User.IsAlive()
        && (ctx.Item.HealsAllHp || ctx.Item.HealAmount is > 0)
        && ctx.User.Attributes.HP < ctx.User.Attributes.MaxHP;

    public void Apply(ItemEffectContext ctx)
    {
        int before = ctx.User.Attributes.HP;
        int amount = ctx.Item.HealsAllHp
            ? ctx.User.Attributes.MaxHP - before
            : ctx.Item.HealAmount ?? 0;
        ctx.User.Attributes.ReceiveHealing(amount); // caps at MaxHP
        ctx.Emitter?.Emit(
            new Healed(ctx.User.Name, ctx.User.Attributes.HP - before, ctx.User.Attributes.HP)
        );

        // Full Restore also cures any major status (Gen 1). Confusion is volatile and not cured by items.
        if (ctx.Item.CuresAllStatus && ctx.User.Battle.Status != StatusCondition.None)
            ClearStatus(ctx.User, ctx.Emitter);
    }

    internal static void ClearStatus(Creature user, IBattleEventEmitter? emitter)
    {
        var was = user.Battle.Status;
        user.Battle.Status = StatusCondition.None;
        user.Battle.SleepTurns = 0;
        user.Battle.ToxicCounter = 1; // reset Gen 1 Toxic escalation baseline
        emitter?.Emit(new StatusCleared(user.Name, was));
    }
}

/// <summary>Antidote / Burn Heal / Ice Heal / Awakening / Paralyze Heal / Full Heal — cure major status.</summary>
public sealed class StatusCureItemEffect : IItemEffect
{
    public ItemCategory Category => ItemCategory.StatusCure;

    public bool CanApply(ItemEffectContext ctx)
    {
        if (!ctx.User.IsAlive() || ctx.User.Battle.Status == StatusCondition.None)
            return false;
        if (ctx.Item.CuresAllStatus)
            return true;
        // A single-status cure works on the matching condition. Gen 1 Toxic shows as BadPoison; an
        // Antidote (cures Poison) also clears BadPoison.
        return ctx.Item.CuredStatus is { } cure
            && (
                ctx.User.Battle.Status == cure
                || (
                    cure == StatusCondition.Poison
                    && ctx.User.Battle.Status == StatusCondition.BadPoison
                )
            );
    }

    public void Apply(ItemEffectContext ctx) =>
        HealingItemEffect.ClearStatus(ctx.User, ctx.Emitter);
}

/// <summary>Ether / Max Ether (one move) and Elixir / Max Elixir (all moves) — restore PP.</summary>
public sealed class PpRestoreItemEffect : IItemEffect
{
    public ItemCategory Category => ItemCategory.PpRestore;

    public bool CanApply(ItemEffectContext ctx)
    {
        if (!ctx.User.IsAlive() || ctx.User.MoveSet.Count == 0)
            return false;
        if (ctx.Item.RestoresPpAllMoves)
            return ctx.User.MoveSet.Any(m => m.PowerPointsCurrent < m.Base.PowerPointsMax);

        // Single-move: a valid target slot that isn't already full.
        return ctx.TargetMoveSlot is { } slot
            && slot >= 0
            && slot < ctx.User.MoveSet.Count
            && ctx.User.MoveSet[slot].PowerPointsCurrent
                < ctx.User.MoveSet[slot].Base.PowerPointsMax;
    }

    public void Apply(ItemEffectContext ctx)
    {
        if (ctx.Item.RestoresPpAllMoves)
        {
            foreach (var move in ctx.User.MoveSet)
                RestoreMove(ctx, move);
        }
        else if (ctx.TargetMoveSlot is { } slot)
        {
            RestoreMove(ctx, ctx.User.MoveSet[slot]);
        }
    }

    private static void RestoreMove(ItemEffectContext ctx, PokemonAttack move)
    {
        if (move.PowerPointsCurrent >= move.Base.PowerPointsMax)
            return;
        move.PowerPointsCurrent = ctx.Item.RestoresAllPp
            ? move.Base.PowerPointsMax
            : Math.Min(
                move.Base.PowerPointsMax,
                move.PowerPointsCurrent + (ctx.Item.PpRestoreAmount ?? 0)
            );
        ctx.Emitter?.Emit(
            new PpRestored(ctx.User.Name, move.Base.Name ?? "", move.PowerPointsCurrent)
        );
    }
}

/// <summary>X Attack / Defense / Speed / Special / Accuracy — raise a stat stage in battle.</summary>
public sealed class XItemEffect : IItemEffect
{
    public ItemCategory Category => ItemCategory.BattleStatBoost;

    // dire-hit (crit) and guard-spec (Mist) carry no StatBoostStat — their effect is deferred, so they
    // report no effect here. An X-item with the stat already at +6 also won't change anything.
    public bool CanApply(ItemEffectContext ctx) =>
        ctx.User.IsAlive()
        && ctx.Item.StatBoostStat is { } stat
        && (ctx.Item.StatBoostStages ?? 0) != 0
        && ctx.User.Battle.Stages.Of(stat) < 6;

    public void Apply(ItemEffectContext ctx)
    {
        var stat = ctx.Item.StatBoostStat!.Value;
        int delta = ctx.Item.StatBoostStages ?? 0;
        int newStage = ctx.User.Battle.Stages.Raise(stat, delta);
        ctx.Emitter?.Emit(new StatStageChanged(ctx.User.Name, stat.ToString(), delta, newStage));
    }
}

/// <summary>Registry of in-battle item effects, keyed by the item category that drives each.</summary>
public static class ItemEffects
{
    /// <summary>
    /// Every in-battle item effect. Revive and Ball are intentionally absent: Revive needs a fainted
    /// party member (no party yet) and Ball needs the catch mechanic (deferred, gated on Encounter
    /// Logic) — <see cref="For"/> returns null for them, so <see cref="ItemAction"/> reports no effect.
    /// </summary>
    public static readonly IReadOnlyList<IItemEffect> All = new IItemEffect[]
    {
        new HealingItemEffect(),
        new StatusCureItemEffect(),
        new PpRestoreItemEffect(),
        new XItemEffect(),
    };

    private static readonly IReadOnlyDictionary<ItemCategory, IItemEffect> ByCategory =
        All.ToDictionary(e => e.Category);

    /// <summary>The effect for <paramref name="category"/>, or null if items of that category have no
    /// in-battle effect yet (Ball, Revive, Other).</summary>
    public static IItemEffect? For(ItemCategory category) =>
        ByCategory.TryGetValue(category, out var effect) ? effect : null;
}
