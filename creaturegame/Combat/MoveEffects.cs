using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// Everything a post-damage move effect needs to run, without reaching into <see cref="AttackAction"/>'s
/// internals. The effect reads the resolved move, the damage the attack just dealt, and the shared seams
/// (rules/rng/emitter), mutates battle state, and emits its own events. Damage-dealing effects (Counter,
/// Bide's unleash routes elsewhere) call <see cref="DealDamage"/> so the Substitute soak, Bide
/// accumulation and Counter recording stay centralized in one place.
/// </summary>
public sealed class MoveEffectContext
{
    public required Creature Source { get; init; }
    public required Creature Target { get; init; }

    /// <summary>The resolved move (its base data — name, type, …).</summary>
    public required Attack Attack { get; init; }

    /// <summary>
    /// The selected move wrapper, or null for Struggle. Binding locks the user into re-using it and Mimic
    /// replaces its base, so they need the wrapper, not just the base data.
    /// </summary>
    public PokemonAttack? SelectedMove { get; init; }

    /// <summary>Total damage the attack dealt this turn — gates recoil and the binding trap.</summary>
    public required int Damage { get; init; }

    public required IBattleRules Rules { get; init; }
    public required IRandomSource Rng { get; init; }
    public IBattleEventEmitter? Emitter { get; init; }

    /// <summary>
    /// True while a foe-directed secondary should be blocked because the Target was hiding behind a
    /// Substitute when the move connected (Gen 1: the decoy shields the user from confusion here).
    /// </summary>
    public required bool TargetShieldedBySubstitute { get; init; }

    /// <summary>
    /// Applies damage to the Target through <see cref="AttackAction"/>'s shared helper (Substitute soak +
    /// Bide accumulation + Counter recording). Signature mirrors <c>DealDamageToTarget</c>:
    /// (damage, effectiveness, isCrit, counterableType) → true when the real creature took the hit.
    /// </summary>
    public required Func<int, double, bool, DamageType?, bool> DealDamage { get; init; }
}

/// <summary>
/// A post-damage move effect — the "secondary" half of an attack that fires after the damage step:
/// Haze, Counter, Reflect, Transform, Rest, Substitute, … Each effect owns its own behaviour behind this
/// hook (mirroring <see cref="ILockInMechanic"/>) rather than being branched inline in a single giant
/// switch in <see cref="AttackAction"/>. Exactly one effect runs per move (keyed by <see cref="MoveEffect"/>),
/// so adding one is a new class in <see cref="MoveEffects.All"/>, not another switch arm.
/// </summary>
public interface IMoveEffect
{
    /// <summary>The move effect this implementation drives.</summary>
    MoveEffect Effect { get; }

    /// <summary>Run the effect after the move's damage step has resolved.</summary>
    void Apply(MoveEffectContext ctx);
}

/// <summary>Haze: wipe both combatants' stat stages (and volatile state).</summary>
public sealed class HazeEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Haze;

    public void Apply(MoveEffectContext ctx)
    {
        ctx.Source.ResetBattleState();
        ctx.Target.ResetBattleState();
        ctx.Emitter?.Emit(new HazeClearedStages());
    }
}

/// <summary>Flinch: a chance (on a damaging move) to make the target lose its turn this round.</summary>
public sealed class FlinchEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Flinch;

    public void Apply(MoveEffectContext ctx)
    {
        if (ctx.Target.IsAlive())
        {
            int chance = ctx.Rules.GetSecondaryEffectChance(ctx.Attack, SecondaryEffectKind.Flinch);
            if (ctx.Rules.SecondaryHits(chance, ctx.Rng))
                ctx.Target.Battle.IsFlinched = true;
        }
    }
}

/// <summary>Leech Seed: plant a seed that drains the target each turn.</summary>
public sealed class LeechSeedEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.LeechSeed;

    public void Apply(MoveEffectContext ctx)
    {
        // Deliberately NOT gated on TargetShieldedBySubstitute, unlike the foe's status / stat /
        // confusion secondaries: in the English (localized) Gen 1 games Leech Seed lands THROUGH
        // a Substitute. (It's blocked only in the Japanese games and Pokémon Stadium; Gen 2+
        // blocks it everywhere.) This engine targets English RBY, so the seed takes hold here —
        // do not "consistency-fix" this to match the other sub-shielded effects.
        if (ctx.Target.IsAlive() && !ctx.Rules.CanBeLeechSeeded(ctx.Target))
            ctx.Emitter?.Emit(new MoveHadNoEffect(ctx.Target.Name, ctx.Attack.Name ?? "")); // Grass-types are immune
        else if (!ctx.Target.Battle.HasLeechSeed && ctx.Target.IsAlive())
        {
            ctx.Target.Battle.HasLeechSeed = true;
            ctx.Emitter?.Emit(new LeechSeedApplied(ctx.Target.Name));
        }
    }
}

/// <summary>Binding (Wrap, Bind, Clamp, Fire Spin): start the Gen 1 partial trap on a hit.</summary>
public sealed class BindingEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Binding;

    public void Apply(MoveEffectContext ctx)
    {
        // Gen 1 partial trap: on a hit, trap the VICTIM (loses its turn for 2–5 turns, no residual chip)
        // and lock THIS user into re-using the move — BindingMechanic.ForcedMove reads the victim's counter
        // to keep forcing it. Guarded so a forced continuation hit doesn't re-roll the duration. Damage is
        // recalculated per re-hit, not locked to the first (a documented Gen 1 simplification).
        if (ctx.Target.Battle.BindingTurnsRemaining == 0 && ctx.Damage > 0 && ctx.Target.IsAlive())
        {
            ctx.Target.Battle.BindingTurnsRemaining = ctx.Rules.RollBindingTurns();
            ctx.Source.Battle.BindingMove = ctx.SelectedMove;
            ctx.Source.Battle.BindingTarget = ctx.Target;
            ctx.Emitter?.Emit(new BindingStarted(ctx.Target.Name, ctx.Attack.Name ?? ""));
        }
    }
}

/// <summary>Pay Day: scatter coins on a hit (collected after the battle).</summary>
public sealed class PayDayEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.PayDay;

    public void Apply(MoveEffectContext ctx)
    {
        // Coins are collected after the battle — no economy yet, so the event is the whole mechanic.
        ctx.Emitter?.Emit(
            new CoinsScattered(ctx.Source.Name, ctx.Rules.PayDayCoinMultiplier * ctx.Source.Level)
        );
    }
}

/// <summary>Recoil (Take Down, Double-Edge): the user takes back a fraction of the damage it dealt.</summary>
public sealed class RecoilEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Recoil;

    public void Apply(MoveEffectContext ctx)
    {
        if (ctx.Damage > 0 && ctx.Source.IsAlive())
        {
            int recoil = ctx.Rules.CalculateRecoilDamage(ctx.Damage);
            ctx.Source.Attributes.ReceiveDamage(recoil);
            ctx.Emitter?.Emit(new RecoilDamage(ctx.Source.Name, recoil, ctx.Source.Attributes.HP));
        }
    }
}

/// <summary>Disable: lock one of the target's moves out of selection for several turns.</summary>
public sealed class DisableEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Disable;

    public void Apply(MoveEffectContext ctx)
    {
        // Disable locks one of the target's moves out for several turns; fails if it already has one
        // disabled or no usable move. Duration is gen-variable ⇒ the seam (RollDisableTurns). *Which* move
        // is also gen-variable (Gen 1/2 pick a random PP-bearing move, below; Gen 3+ the last-used) but kept
        // inline until Gen 3 needs it. The lock is enforced at selection time (Battle/IBattleInput skip
        // DisabledMove); the counter ticks down end-of-turn (StatusResolver) and re-enables.
        if (ctx.Target.IsAlive() && ctx.Target.Battle.DisableTurnsRemaining == 0)
        {
            var disableable = ctx.Target.MoveSet.Where(m => m.PowerPointsCurrent > 0).ToList();
            if (disableable.Count > 0)
            {
                var locked = disableable[ctx.Rng.Next(disableable.Count)];
                ctx.Target.Battle.DisabledMove = locked;
                ctx.Target.Battle.DisableTurnsRemaining = ctx.Rules.RollDisableTurns();
                ctx.Emitter?.Emit(new MoveDisabled(ctx.Target.Name, locked.Base.Name ?? ""));
            }
        }
    }
}

/// <summary>Counter: return double the damage the user last took from a Normal/Fighting move.</summary>
public sealed class CounterEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Counter;

    public void Apply(MoveEffectContext ctx)
    {
        // Gen 1: returns 2× the damage the user last took from a Normal/Fighting move. The −5 priority
        // (move data) resolves Counter after the foe's hit. Fails if no qualifying damage was taken — Gen 1
        // keeps the last value until overwritten, so it can fire off a previous turn (a quirk we preserve).
        // Any damaging move records its type via DealDamageToTarget, so fixed/level-based Normal/Fighting
        // moves (Sonic Boom, Seismic Toss, Super Fang) are counterable too; only Bide's unleash opts out.
        // Which type qualifies is gen-variable (Gen 1: Normal/Fighting; Gen 2+: physical category) ⇒ the
        // seam. Type immunity (Fighting ⇒ Ghost) is already handled by the pure-status guard above (Counter's
        // BaseDamage 0 short-circuits to MoveHadNoEffect before here).
        if (
            ctx.Target.IsAlive()
            && ctx.Source.Battle.LastDamageTaken > 0
            && ctx.Rules.CounterQualifies(ctx.Source.Battle.LastDamageType)
        )
        {
            int countered = ctx.Source.Battle.LastDamageTaken * 2;
            ctx.DealDamage(countered, 1.0, false, ctx.Attack.DamageType);
        }
        else
        {
            ctx.Emitter?.Emit(new MoveMissed(ctx.Source.Name, ctx.Attack.Name ?? ""));
        }
    }
}

/// <summary>Mist: the opponent can't lower the user's stats until the battle ends.</summary>
public sealed class MistEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Mist;

    public void Apply(MoveEffectContext ctx)
    {
        // Self-targeting; the opponent's stat-drops are then blocked in TryApplyStatEffect.
        if (!ctx.Source.Battle.HasMist)
        {
            ctx.Source.Battle.HasMist = true;
            ctx.Emitter?.Emit(new MistApplied(ctx.Source.Name));
        }
    }
}

/// <summary>Reflect: doubles the user's Defense vs physical damage until the battle ends.</summary>
public sealed class ReflectEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Reflect;

    public void Apply(MoveEffectContext ctx)
    {
        if (!ctx.Source.Battle.HasReflect)
        {
            ctx.Source.Battle.HasReflect = true;
            ctx.Emitter?.Emit(new ScreenApplied(ctx.Source.Name, "Reflect"));
        }
    }
}

/// <summary>Light Screen: doubles the user's Special vs special damage until the battle ends.</summary>
public sealed class LightScreenEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.LightScreen;

    public void Apply(MoveEffectContext ctx)
    {
        if (!ctx.Source.Battle.HasLightScreen)
        {
            ctx.Source.Battle.HasLightScreen = true;
            ctx.Emitter?.Emit(new ScreenApplied(ctx.Source.Name, "Light Screen"));
        }
    }
}

/// <summary>Focus Energy: self-targeting crit modifier (Gen 1's bugged ÷4).</summary>
public sealed class FocusEnergyEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.FocusEnergy;

    public void Apply(MoveEffectContext ctx)
    {
        // The bugged ÷4 itself is applied in Gen1BattleRules.GetCritChance.
        if (!ctx.Source.Battle.HasFocusEnergy)
        {
            ctx.Source.Battle.HasFocusEnergy = true;
            ctx.Emitter?.Emit(new FocusEnergyApplied(ctx.Source.Name));
        }
    }
}

/// <summary>Heal (Recover, Soft-Boiled): restore a fraction of max HP to the user.</summary>
public sealed class HealEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Heal;

    public void Apply(MoveEffectContext ctx)
    {
        // The heal fraction is gen-variable, so it comes from the rules seam (ReceiveHealing caps at max).
        if (ctx.Source.IsAlive())
        {
            int hpBefore = ctx.Source.Attributes.HP;
            int heal = Math.Max(
                1,
                (int)(ctx.Source.Attributes.MaxHP * ctx.Rules.RecoverHealFraction)
            );
            ctx.Source.Attributes.ReceiveHealing(heal);
            // Report the amount actually restored (ReceiveHealing caps at max), not the request.
            ctx.Emitter?.Emit(
                new Healed(
                    ctx.Source.Name,
                    ctx.Source.Attributes.HP - hpBefore,
                    ctx.Source.Attributes.HP
                )
            );
        }
    }
}

/// <summary>Mimic: copy a random move from the target's set for the rest of the battle.</summary>
public sealed class MimicEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Mimic;

    public void Apply(MoveEffectContext ctx)
    {
        // Gen 1: copy a random move from the target's set; it replaces Mimic for the rest of the battle
        // (Battle restores it on end). Copying is this turn's action (no damage); the move is usable on
        // later turns. Fails if already mimicked or the target has no copyable move.
        if (
            ctx.Target.IsAlive()
            && ctx.SelectedMove != null
            && ctx.Source.Battle.MimicWrapper == null
        )
        {
            var copyable = ctx
                .Target.MoveSet.Where(m =>
                    m.Base.Effect != MoveEffect.Mimic && m.Base.Name != "struggle"
                )
                .ToList();
            if (copyable.Count > 0)
            {
                var chosen = copyable[ctx.Rng.Next(copyable.Count)].Base;
                ctx.Source.Battle.MimicWrapper = ctx.SelectedMove;
                ctx.Source.Battle.MimicOriginalBase = ctx.SelectedMove.Base;
                ctx.SelectedMove.Base = chosen;
                ctx.Emitter?.Emit(new MimicLearned(ctx.Source.Name, chosen.Name ?? ""));
            }
        }
    }
}

/// <summary>Transform (Ditto/Mew): the user becomes a copy of the target.</summary>
public sealed class TransformEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Transform;

    public void Apply(MoveEffectContext ctx)
    {
        // Gen 1: the user becomes a copy of the target — types, the four non-HP stats, current stat stages,
        // SpeciesId and full moveset (each copied move at 5 PP, or its max if lower). HP/MaxHP/level stay the
        // user's. Undone at battle end (RestoreOriginalIdentity via ResetBattleState). Self-affecting, no
        // damage; fails on a fainted target. Snapshot taken before mutating so the original survives even if
        // Conversion later changes the type too.
        if (ctx.Target.IsAlive())
        {
            ctx.Source.SnapshotIdentityForMutation();
            ctx.Source.Type1 = ctx.Target.Type1;
            ctx.Source.Type2 = ctx.Target.Type2;
            ctx.Source.SpeciesId = ctx.Target.SpeciesId;
            ctx.Source.Attributes.Attack = ctx.Target.Attributes.Attack;
            ctx.Source.Attributes.Defense = ctx.Target.Attributes.Defense;
            ctx.Source.Attributes.Special = ctx.Target.Attributes.Special;
            ctx.Source.Attributes.Speed = ctx.Target.Attributes.Speed;
            ctx.Source.Battle.Stages = ctx.Target.Battle.Stages.Copy();
            // Copy-on-write: build the copied moveset, then swap it onto Source via the engine entry
            // point so a concurrent CHECK POKEMON read never enumerates a half-built list (see the
            // MoveSet field comment on Creature).
            ctx.Source.SetMoveSet(
                ctx.Target.MoveSet.Select(copied => new PokemonAttack(copied.Base)
                {
                    PowerPointsCurrent = Math.Min(5, copied.Base.PowerPointsMax),
                })
            );
            ctx.Emitter?.Emit(
                new TransformedInto(ctx.Source.Name, ctx.Target.Name, ctx.Target.SpeciesId)
            );
        }
    }
}

/// <summary>Conversion: the user copies the target's types onto itself (Gen 1 behavior).</summary>
public sealed class ConversionEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Conversion;

    public void Apply(MoveEffectContext ctx)
    {
        // Gen 1: the user copies the target's types (Gen 2+ instead picks one of the user's own moves' type
        // — a different mechanic, kept inline until Gen 2 can exercise a seam). Self-affecting, no damage;
        // undone at battle end like Transform. Snapshot first so Transform-then-Conversion still restores the
        // true original.
        if (ctx.Target.IsAlive())
        {
            ctx.Source.SnapshotIdentityForMutation();
            ctx.Source.Type1 = ctx.Target.Type1;
            ctx.Source.Type2 = ctx.Target.Type2;
            ctx.Emitter?.Emit(
                new ConvertedType(ctx.Source.Name, ctx.Source.Type1 ?? DamageType.Normal)
            );
        }
    }
}

/// <summary>Rest: fully restore HP, cure status, then force sleep for a fixed number of turns.</summary>
public sealed class RestEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Rest;

    public void Apply(MoveEffectContext ctx)
    {
        // Gen 1: fully restore HP, cure any major status, then force sleep for a fixed number of turns
        // (gen-variable ⇒ RestSleepTurns, distinct from the random RollSleepTurns). Fails if already at full
        // HP — a state precondition, so it reuses MoveMissed like Counter/Dream Eater, not the type-based
        // MoveHadNoEffect. Self-targeting ⇒ the foe-immunity guard never blocks it.
        if (ctx.Source.Attributes.HP >= ctx.Source.Attributes.MaxHP)
        {
            ctx.Emitter?.Emit(new MoveMissed(ctx.Source.Name, ctx.Attack.Name ?? ""));
            return;
        }
        int restored = ctx.Source.Attributes.MaxHP - ctx.Source.Attributes.HP;
        ctx.Source.Attributes.ReceiveHealing(restored); // heals exactly to full
        // Sleep overwrites any prior major status (Gen 1 allows only one) — Rest's documented cure.
        ctx.Source.Battle.Status = StatusCondition.Sleep;
        ctx.Source.Battle.SleepTurns = ctx.Rules.RestSleepTurns;
        ctx.Emitter?.Emit(new Healed(ctx.Source.Name, restored, ctx.Source.Attributes.HP));
        ctx.Emitter?.Emit(new StatusApplied(ctx.Source.Name, StatusCondition.Sleep));
    }
}

/// <summary>Substitute: spend ¼ max HP to raise a decoy that soaks the foe's hits.</summary>
public sealed class SubstituteEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Substitute;

    public void Apply(MoveEffectContext ctx)
    {
        // Gen 1: spend floor(maxHP/4) HP to raise a decoy with floor(maxHP/4)+1 HP that soaks the foe's hits
        // (absorption in DealDamageToTarget; status/stat shielding in the Try* methods). Fails if one is
        // already up or the user can't pay (HP ≤ cost) — a state precondition, so it reuses MoveMissed like
        // Counter/Rest. The ¼ cost is gen-invariant (litmus "would Gen 2 change it?" → no) ⇒ inline, not the
        // seam. Self-targeting ⇒ the foe-immunity guard never blocks it.
        if (ctx.Source.Battle.SubstituteHp > 0)
        {
            ctx.Emitter?.Emit(new MoveMissed(ctx.Source.Name, ctx.Attack.Name ?? ""));
            return;
        }
        int cost = ctx.Source.Attributes.MaxHP / 4;
        if (ctx.Source.Attributes.HP <= cost)
        {
            ctx.Emitter?.Emit(new MoveMissed(ctx.Source.Name, ctx.Attack.Name ?? ""));
            return;
        }
        ctx.Source.Attributes.ReceiveDamage(cost);
        ctx.Source.Battle.SubstituteHp = cost + 1;
        ctx.Emitter?.Emit(new SubstitutePutUp(ctx.Source.Name, ctx.Source.Battle.SubstituteHp));
    }
}

/// <summary>Splash: does nothing by design — Gen 1's "But nothing happened!".</summary>
public sealed class SplashEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Splash;

    public void Apply(MoveEffectContext ctx)
    {
        // No-op by design; invariant across generations, so it stays inline rather than on the seam.
        ctx.Emitter?.Emit(new ButNothingHappened(ctx.Source.Name));
    }
}

/// <summary>Confuse: confuse the target (pure or as a secondary on a damaging move).</summary>
public sealed class ConfuseEffect : IMoveEffect
{
    public MoveEffect Effect => MoveEffect.Confuse;

    public void Apply(MoveEffectContext ctx)
    {
        // Confusion is independent of major status (both can apply) and doesn't stack (Gen 1: only if not
        // already confused). EffectChance gates secondary confusion on damaging moves (Psybeam 10%); pure
        // confusion moves (Supersonic, Confuse Ray) have no chance ⇒ always land. A Substitute shields the
        // foe's confusion while it stands (Gen 1).
        if (
            ctx.Target.IsAlive()
            && ctx.Target.Battle.ConfusedTurns == 0
            && !ctx.TargetShieldedBySubstitute
        )
        {
            int chance = ctx.Rules.GetSecondaryEffectChance(
                ctx.Attack,
                SecondaryEffectKind.Confuse
            );
            if (ctx.Rules.SecondaryHits(chance, ctx.Rng))
            {
                ctx.Target.Battle.ConfusedTurns = ctx.Rules.RollConfusionTurns();
                ctx.Emitter?.Emit(new ConfusionStarted(ctx.Target.Name));
            }
        }
    }
}

/// <summary>The registry of post-damage move effects, keyed by the move effect that drives each.</summary>
public static class MoveEffects
{
    /// <summary>Every post-damage effect. Adding an effect here makes it routable — no second map to sync.</summary>
    public static readonly IReadOnlyList<IMoveEffect> All = new IMoveEffect[]
    {
        new HazeEffect(),
        new FlinchEffect(),
        new LeechSeedEffect(),
        new BindingEffect(),
        new PayDayEffect(),
        new RecoilEffect(),
        new DisableEffect(),
        new CounterEffect(),
        new MistEffect(),
        new ReflectEffect(),
        new LightScreenEffect(),
        new FocusEnergyEffect(),
        new HealEffect(),
        new MimicEffect(),
        new TransformEffect(),
        new ConversionEffect(),
        new RestEffect(),
        new SubstituteEffect(),
        new SplashEffect(),
        new ConfuseEffect(),
    };

    // Effect → effect, derived from All so each effect's own Effect is the single source of truth:
    // an effect added to All is routable here without touching a second, hand-synced map.
    private static readonly IReadOnlyDictionary<MoveEffect, IMoveEffect> ByEffect =
        All.ToDictionary(e => e.Effect);

    /// <summary>The effect that drives <paramref name="effect"/>, or null if it has no post-damage effect.</summary>
    public static IMoveEffect? For(MoveEffect effect) =>
        ByEffect.TryGetValue(effect, out var moveEffect) ? moveEffect : null;
}
