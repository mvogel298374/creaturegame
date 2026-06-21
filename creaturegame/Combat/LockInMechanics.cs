using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// Everything a lock-in mechanic needs to run one turn of its move, without reaching into
/// <see cref="AttackAction"/>'s internals. The mechanic mutates <see cref="Source"/>'s lock-in state,
/// emits its own events, and (for Bide) returns the damage for the action to deal.
/// </summary>
public sealed class LockInContext
{
    public required Creature Source { get; init; }
    public required Creature Target { get; init; }
    public required PokemonAttack Move { get; init; }
    public required string MoveName { get; init; }
    public required IBattleRules Rules { get; init; }
    public IBattleEventEmitter? Emitter { get; init; }

    /// <summary>
    /// True when this turn continues an already-started lock — PP was spent on the first turn, so a
    /// mechanic uses it to tell "begin the lock" from "carry it on."
    /// </summary>
    public required bool IsContinuation { get; init; }
}

/// <summary>Whether the turn keeps running the normal attack pipeline after a lock-in hook.</summary>
public enum LockInFlow
{
    /// <summary>Keep going — announce the move / run the normal attack.</summary>
    Proceed,

    /// <summary>The mechanic finished the turn here (charging, storing, or Bide's unleash).</summary>
    Halt,
}

/// <summary>
/// A lock-in hook's decision. <see cref="UnleashDamage"/> (&gt; 0, only with <see cref="LockInFlow.Halt"/>)
/// tells <see cref="AttackAction"/> to deal that much typeless damage through the shared damage helper
/// before ending the turn — Bide's release.
/// </summary>
public readonly record struct LockInResult(LockInFlow Flow, int UnleashDamage = 0)
{
    public static readonly LockInResult Proceed = new(LockInFlow.Proceed);
    public static readonly LockInResult Halt = new(LockInFlow.Halt);

    public static LockInResult Unleash(int damage) => new(LockInFlow.Halt, damage);
}

/// <summary>
/// A move that, once started, auto-repeats (Battle force-selects it) and doesn't re-spend PP until it
/// ends. The four Gen 1 lock-ins (two-turn, rampage, rage, bide) differ sharply turn-to-turn — one
/// charges then strikes, one stores then unleashes, two strike every turn — so each owns its own
/// charge/store/attack/end behaviour through these hooks rather than being branched inline in
/// <see cref="AttackAction"/>.
/// </summary>
public interface ILockInMechanic
{
    /// <summary>The move effect this mechanic drives.</summary>
    MoveEffect Effect { get; }

    /// <summary>
    /// The move to force this turn while <paramref name="c"/> is locked into this mechanic, or null
    /// when it isn't. Battle uses this to bypass <see cref="IBattleInput"/> for a locked combatant.
    /// </summary>
    PokemonAttack? ForcedMove(Creature c);

    /// <summary>Whether the creature is mid-lock (a continuation turn). Defaults to "has a forced move."</summary>
    bool IsLockedIn(Creature c) => ForcedMove(c) != null;

    /// <summary>
    /// Before "MoveUsed" is announced: charge / store / commit. <see cref="LockInFlow.Halt"/> ends the
    /// turn now (two-turn charge, bide store); <see cref="LockInFlow.Proceed"/> announces the move and
    /// continues.
    /// </summary>
    LockInResult OnCommit(LockInContext ctx) => LockInResult.Proceed;

    /// <summary>
    /// After "MoveUsed", before the normal attack pipeline: set up counters (rampage/rage) and
    /// <see cref="LockInFlow.Proceed"/> to run the attack, or do the mechanic's own damage and
    /// <see cref="LockInFlow.Halt"/> (bide unleash).
    /// </summary>
    LockInResult OnRelease(LockInContext ctx) => LockInResult.Proceed;

    /// <summary>After the attack resolves (hit OR miss): finalize the lock — rampage self-confuse.</summary>
    void OnTurnEnd(LockInContext ctx) { }
}

/// <summary>Two-turn moves (Fly, Dig, Sky Attack…): charge turn winds up, release turn strikes.</summary>
public sealed class TwoTurnMechanic : ILockInMechanic
{
    public MoveEffect Effect => MoveEffect.TwoTurn;

    public PokemonAttack? ForcedMove(Creature c) =>
        c.Battle.IsTwoTurnCharging ? c.Battle.ChargingMove : null;

    public LockInResult OnCommit(LockInContext ctx)
    {
        if (!ctx.IsContinuation)
        {
            // Charge turn: wind up and defer the strike to next turn.
            ctx.Source.Battle.IsTwoTurnCharging = true;
            ctx.Source.Battle.ChargingMove = ctx.Move;
            ctx.Emitter?.Emit(new ChargingUp(ctx.Source.Name, ctx.MoveName));
            return LockInResult.Halt;
        }

        // Release turn: clear the charge and let the attack run.
        ctx.Source.Battle.IsTwoTurnCharging = false;
        ctx.Source.Battle.ChargingMove = null;
        return LockInResult.Proceed;
    }
}

/// <summary>Rampage (Thrash, Petal Dance): strikes every turn, then self-confuses when the lock ends.</summary>
public sealed class RampageMechanic : ILockInMechanic
{
    public MoveEffect Effect => MoveEffect.Rampage;

    public PokemonAttack? ForcedMove(Creature c) =>
        c.Battle.RampageTurnsRemaining > 0 ? c.Battle.RampageMove : null;

    public LockInResult OnRelease(LockInContext ctx)
    {
        if (!ctx.IsContinuation)
        {
            ctx.Source.Battle.RampageTurnsRemaining = ctx.Rules.RollRampageTurns();
            ctx.Source.Battle.RampageMove = ctx.Move;
        }
        ctx.Source.Battle.RampageTurnsRemaining--; // a missed turn still counts toward the lock
        return LockInResult.Proceed;
    }

    public void OnTurnEnd(LockInContext ctx)
    {
        if (ctx.Source.Battle.RampageTurnsRemaining > 0)
            return;
        ctx.Source.Battle.RampageMove = null;
        if (ctx.Source.IsAlive() && ctx.Source.Battle.ConfusedTurns == 0)
        {
            ctx.Source.Battle.ConfusedTurns = ctx.Rules.RollConfusionTurns();
            ctx.Emitter?.Emit(new ConfusionStarted(ctx.Source.Name));
        }
    }
}

/// <summary>Rage: once used, the user is locked in forever (the on-hit Attack raise lives in the damage path).</summary>
public sealed class RageMechanic : ILockInMechanic
{
    public MoveEffect Effect => MoveEffect.Rage;

    public PokemonAttack? ForcedMove(Creature c) => c.Battle.IsRaging ? c.Battle.RageMove : null;

    public LockInResult OnRelease(LockInContext ctx)
    {
        if (!ctx.IsContinuation)
        {
            ctx.Source.Battle.IsRaging = true;
            ctx.Source.Battle.RageMove = ctx.Move;
        }
        return LockInResult.Proceed;
    }
}

/// <summary>Bide: commits for 2–3 turns storing damage taken, then unleashes double.</summary>
public sealed class BideMechanic : ILockInMechanic
{
    public MoveEffect Effect => MoveEffect.Bide;

    public PokemonAttack? ForcedMove(Creature c) =>
        c.Battle.BideTurnsRemaining > 0 ? c.Battle.BideMove : null;

    public LockInResult OnCommit(LockInContext ctx)
    {
        if (!ctx.IsContinuation)
        {
            ctx.Source.Battle.BideTurnsRemaining = ctx.Rules.RollBideTurns();
            ctx.Source.Battle.BideDamageAccumulated = 0;
            ctx.Source.Battle.BideMove = ctx.Move;
        }
        ctx.Source.Battle.BideTurnsRemaining--;
        if (ctx.Source.Battle.BideTurnsRemaining > 0)
        {
            ctx.Emitter?.Emit(new BideStoring(ctx.Source.Name));
            return LockInResult.Halt;
        }
        return LockInResult.Proceed; // committed turns done — release this turn
    }

    public LockInResult OnRelease(LockInContext ctx)
    {
        // Unleash double the absorbed damage. Typeless and never recorded as counterable (it passes no
        // type to the damage helper); fails if nothing was stored. BideMove clears so Battle stops
        // auto-repeating.
        int unleashed = ctx.Source.Battle.BideDamageAccumulated * ctx.Rules.BideDamageMultiplier;
        ctx.Source.Battle.BideMove = null;
        if (unleashed > 0 && ctx.Target.IsAlive())
            return LockInResult.Unleash(unleashed);

        ctx.Emitter?.Emit(new MoveMissed(ctx.Source.Name, ctx.MoveName));
        return LockInResult.Halt;
    }
}

/// <summary>
/// Binding (Wrap, Bind, Clamp, Fire Spin): Gen 1 partial trap. The binder is locked into re-using the move
/// each turn (dealing its damage) while the trapped foe can't act. <see cref="AttackAction"/> STARTS the trap
/// on a hit (setting the victim's <c>BindingTurnsRemaining</c> + this binder's <c>BindingMove</c>/
/// <c>BindingTarget</c>); this mechanic only force-repeats while the victim's counter is alive. That counter —
/// ticked down end-of-turn — is the single source of truth, so the binder is freed automatically when it
/// expires (or the victim faints). Unlike rampage, there's NO residual chip and the lock begins on hit, not commit.
/// </summary>
public sealed class BindingMechanic : ILockInMechanic
{
    public MoveEffect Effect => MoveEffect.Binding;

    public PokemonAttack? ForcedMove(Creature c) =>
        c.Battle.BindingTarget is { } victim
        && victim.IsAlive()
        && victim.Battle.BindingTurnsRemaining > 0
            ? c.Battle.BindingMove
            : null;
}

/// <summary>The registry of lock-in mechanics, keyed by the move effect that drives each.</summary>
public static class LockInMechanics
{
    /// <summary>All mechanics, in the order Battle checks them for a forced move.</summary>
    public static readonly IReadOnlyList<ILockInMechanic> All = new ILockInMechanic[]
    {
        new TwoTurnMechanic(),
        new RampageMechanic(),
        new BideMechanic(),
        new RageMechanic(),
        new BindingMechanic(),
    };

    // Effect → mechanic, derived from All so each mechanic's own Effect is the single source of truth:
    // a mechanic added to All is routable here without touching a second, hand-synced map.
    private static readonly IReadOnlyDictionary<MoveEffect, ILockInMechanic> ByEffect =
        All.ToDictionary(m => m.Effect);

    /// <summary>The mechanic that drives <paramref name="effect"/>, or null if it isn't a lock-in move.</summary>
    public static ILockInMechanic? For(MoveEffect effect) =>
        ByEffect.TryGetValue(effect, out var mechanic) ? mechanic : null;
}
