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

    /// <summary>The run party — needed only by the party-targeting items (Revive), which act on a benched member
    /// rather than <see cref="User"/>. Null for the self-targeting items and for legacy single-creature battles.</summary>
    public Party? Party { get; init; }

    /// <summary>The <see cref="Party"/> member index a party-targeting item (Revive) acts on. Null for the
    /// self-targeting items (heal / cure / PP / X-item), which always act on <see cref="User"/>.</summary>
    public int? TargetPartySlot { get; init; }

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

/// <summary>
/// The in-battle "booster" items, all <see cref="ItemCategory.BattleStatBoost"/>, dispatched by item data:
/// the X-items (X Attack/Defense/Speed/Special/Accuracy) raise a stat stage; <b>Dire Hit</b> raises crit (Gen
/// 1: the Focus Energy state — and its famous ÷4 bug, applied in <c>Gen1BattleRules.GetCritChance</c>); and
/// <b>Guard Spec.</b> sets Mist (blocks foe stat drops). Dire Hit / Guard Spec reuse the Focus Energy / Mist
/// volatiles and events so they narrate and wire exactly like the matching moves. (One effect per category —
/// the registry is keyed by category — so the three behaviours live here rather than in separate classes.)
/// </summary>
public sealed class BattleBoostItemEffect : IItemEffect
{
    public ItemCategory Category => ItemCategory.BattleStatBoost;

    public bool CanApply(ItemEffectContext ctx)
    {
        if (!ctx.User.IsAlive())
            return false;

        // Dire Hit / Guard Spec only do anything the first time (the state is a boolean, set-once per battle).
        if (ctx.Item.BoostsCrit)
            return !ctx.User.Battle.HasFocusEnergy;
        if (ctx.Item.SetsMist)
            return !ctx.User.Battle.HasMist;

        // X-item: a stat boost that would actually move the stage (not already capped at +6).
        return ctx.Item.StatBoostStat is { } stat
            && (ctx.Item.StatBoostStages ?? 0) != 0
            && ctx.User.Battle.Stages.Of(stat) < 6;
    }

    public void Apply(ItemEffectContext ctx)
    {
        if (ctx.Item.BoostsCrit)
        {
            ctx.User.Battle.HasFocusEnergy = true;
            ctx.Emitter?.Emit(new FocusEnergyApplied(ctx.User.Name));
            return;
        }

        if (ctx.Item.SetsMist)
        {
            ctx.User.Battle.HasMist = true;
            ctx.Emitter?.Emit(new MistApplied(ctx.User.Name));
            return;
        }

        var stat = ctx.Item.StatBoostStat!.Value;
        int delta = ctx.Item.StatBoostStages ?? 0;
        int newStage = ctx.User.Battle.Stages.Raise(stat, delta);
        ctx.Emitter?.Emit(new StatStageChanged(ctx.User.Name, stat.ToString(), delta, newStage));
    }
}

/// <summary>
/// Revive / Max Revive — restore a <b>fainted party member</b> to a fraction of its max HP (Gen 1: Revive
/// ½, Max Revive full, off <see cref="Item.RevivePercent"/>). The first and only in-battle item that targets a
/// <em>benched</em> creature rather than the active <see cref="ItemEffectContext.User"/>: it reads
/// <see cref="ItemEffectContext.Party"/> + <see cref="ItemEffectContext.TargetPartySlot"/> and refuses — no
/// announce, no consume (the Gen 1 "won't have any effect" rule) — unless that slot holds a fainted member. The
/// member stays benched; reviving does not switch it in (a mid-battle send-in is the separate forced-switch path).
/// </summary>
public sealed class ReviveItemEffect : IItemEffect
{
    public ItemCategory Category => ItemCategory.Revive;

    public bool CanApply(ItemEffectContext ctx) =>
        Target(ctx) is { } target && !target.IsAlive() && (ctx.Item.RevivePercent ?? 0) > 0;

    public void Apply(ItemEffectContext ctx)
    {
        var target = Target(ctx)!;
        int pct = ctx.Item.RevivePercent ?? 0;
        // Gen 1 fraction-HP math truncates (floor), like Recover/Soft-Boiled (HealEffect) — a Revive on 41 max
        // HP gives 20, not 21. Math.Max(1, …) keeps a revived member ≥ 1 HP on a tiny pool (floor(1·½) = 0 → 1).
        int restored = Math.Max(1, target.Attributes.MaxHP * pct / 100);
        target.Attributes.HP = restored;

        // A revived creature comes back statusless (Gen 1) — clear both the transient battle status and any
        // persisted CarriedStatus (mirrors Creature.FullHeal, minus PP: Revive restores HP only). Without this
        // the roster snapshot below repaints the just-revived member still afflicted, and a carried status would
        // re-apply on its next send-in. A fainted member has no volatile per-battle state to wipe.
        target.Battle.Status = StatusCondition.None;
        target.Battle.SleepTurns = 0;
        target.Battle.ToxicCounter = 1;
        target.CarriedStatus = null;

        ctx.Emitter?.Emit(new Revived(target.Name, restored, target.Attributes.HP));

        // Repaint the roster panel with the benched member's restored HP + cleared status — the ItemUsed/Revived
        // pair narrates the log, but the bench member isn't a nameplate, so its bar only updates off a party
        // snapshot (the same vehicle RecoveryRunEvent uses for the whole-party heal).
        if (ctx.Party is { } party)
            ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(party)));
    }

    // The fainted party member this revive targets: a wired party + an in-range slot. Null when unusable (no
    // party, or a stale / out-of-range slot) — CanApply turns that into the no-effect refusal.
    private static Creature? Target(ItemEffectContext ctx) =>
        ctx.Party is { } party && ctx.TargetPartySlot is { } slot && slot >= 0 && slot < party.Count
            ? party.Members[slot]
            : null;
}

/// <summary>Registry of in-battle item effects, keyed by the item category that drives each.</summary>
public static class ItemEffects
{
    /// <summary>
    /// Every in-battle item effect. Ball is intentionally absent: it needs the catch mechanic (deferred, gated
    /// on Encounter Logic) — <see cref="For"/> returns null for it, so <see cref="ItemAction"/> reports no
    /// effect. Revive now targets a fainted party member (the roster exists), so it is registered here; note its
    /// usability still depends on run state (a fainted member must exist), which its <see cref="IItemEffect.CanApply"/>
    /// checks per use rather than the static category presence this registry exposes.
    /// </summary>
    public static readonly IReadOnlyList<IItemEffect> All = new IItemEffect[]
    {
        new HealingItemEffect(),
        new StatusCureItemEffect(),
        new PpRestoreItemEffect(),
        new BattleBoostItemEffect(),
        new ReviveItemEffect(),
    };

    private static readonly IReadOnlyDictionary<ItemCategory, IItemEffect> ByCategory =
        All.ToDictionary(e => e.Category);

    /// <summary>The effect for <paramref name="category"/>, or null if items of that category have no
    /// in-battle effect yet (Ball, Other).</summary>
    public static IItemEffect? For(ItemCategory category) =>
        ByCategory.TryGetValue(category, out var effect) ? effect : null;
}
