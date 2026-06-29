using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public class AttackAction : IBattleAction
{
    public Creature Source { get; }
    public Creature Target { get; }
    public int Priority { get; }
    private readonly ITypeChart _typeChart;
    private readonly IBattleRules _rules;
    private readonly IBattleEventEmitter? _emitter;
    private readonly IReadOnlyList<Attack> _movePool;
    private readonly IRandomSource _rng;

    // Whether this battle can be fled — gates Roar/Whirlwind (ForceFlee). True for a plain wild battle / the
    // legacy chain; the run layer sets it false for the trainer-analog encounters (Elite/Boss).
    private readonly bool _battleEscapable;

    // Whether the Target had a Substitute up when this move connected — snapshotted pre-damage so a
    // secondary is still shielded on the very hit that breaks the decoy (Gen 1: that hit struck the sub).
    private bool _targetShieldedAtImpact;

    // Null means Struggle — Battle passes null when the source has no selectable move
    // (out of PP, or its only move is Disabled), bypassing IBattleInput.
    private readonly PokemonAttack? _selectedMove;

    public AttackAction(
        Creature source,
        Creature target,
        PokemonAttack? selectedMove,
        ITypeChart typeChart,
        IBattleRules? rules = null,
        IBattleEventEmitter? emitter = null,
        IReadOnlyList<Attack>? movePool = null,
        IRandomSource? rng = null,
        bool battleEscapable = true
    )
    {
        Source = source;
        Target = target;
        _typeChart = typeChart;
        _rules = rules ?? Gen1BattleRules.Instance;
        _emitter = emitter;
        _selectedMove = selectedMove;
        _movePool = movePool ?? Array.Empty<Attack>();
        _rng = rng ?? SystemRandomSource.Instance;
        _battleEscapable = battleEscapable;
        Priority = selectedMove?.Base.Priority ?? 0;
    }

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive())
            return Task.CompletedTask;

        // Recharge: skip this turn and clear the flag (Hyper Beam, etc.)
        if (Source.Battle.IsRecharging)
        {
            Source.Battle.IsRecharging = false;
            _emitter?.Emit(new Recharging(Source.Name));
            return Task.CompletedTask;
        }

        bool usingStruggle = _selectedMove == null;
        Attack attackToUse = usingStruggle ? Source.Struggle : _selectedMove!.Base;

        // Lock-in mechanics (two-turn, rampage, rage, bide) own their per-turn flow — charge/store/
        // unleash/self-confuse — behind ILockInMechanic. A non-lock-in move (or Struggle) leaves lockIn null.
        ILockInMechanic? lockIn = usingStruggle ? null : LockInMechanics.For(attackToUse.Effect);
        bool isLockedInContinuation = lockIn?.IsLockedIn(Source) ?? false;
        LockInContext? lockCtx = lockIn is null
            ? null
            : new LockInContext
            {
                Source = Source,
                Target = Target,
                Move = _selectedMove!,
                MoveName = attackToUse.Name ?? "",
                Rules = _rules,
                Emitter = _emitter,
                IsContinuation = isLockedInContinuation,
            };

        // PP decremented on the first turn only — a lock-in continuation already paid on turn 1.
        if (!usingStruggle && !isLockedInContinuation)
            _selectedMove!.PowerPointsCurrent--;

        // Commit phase: a charge (two-turn) or store (bide) turn ends here, before the move is announced.
        if (lockIn is not null && lockIn.OnCommit(lockCtx!).Flow == LockInFlow.Halt)
            return Task.CompletedTask;

        _emitter?.Emit(new MoveUsed(Source.Name, attackToUse.Name ?? ""));

        // Remember the move actually used so the foe's Mirror Move can copy it. Metronome / Mirror Move
        // don't record themselves — the move they *call* does — so neither is ever the foe's LastMoveUsed
        // (hence the Mirror Move filter below only excludes Struggle, not them).
        if (
            !usingStruggle
            && attackToUse.Effect is not (MoveEffect.Metronome or MoveEffect.MirrorMove)
        )
            Source.Battle.LastMoveUsed = attackToUse;

        // Release phase: the mechanic may unleash its own damage and end the turn (bide), or set up its
        // lock (rampage/rage) and let the normal attack pipeline run.
        if (lockIn is not null)
        {
            var release = lockIn.OnRelease(lockCtx!);
            if (release.Flow == LockInFlow.Halt)
            {
                // Bide's unleash is typeless and routes through the shared helper so a foe Substitute
                // soaks it too; UnleashDamage is 0 when the mechanic already emitted its own outcome.
                if (release.UnleashDamage > 0)
                    DealDamageToTarget(release.UnleashDamage, 1.0, false);
                return Task.CompletedTask;
            }
        }

        // Metronome: pick a random move (excluding Metronome and Mirror Move) and execute it in full.
        // Gen 1: the called move's PP is not consumed (temporary wrapper, no effect on creature's moveset).
        if (!usingStruggle && attackToUse.Effect == MoveEffect.Metronome && _movePool.Count > 0)
        {
            var eligible = _movePool
                .Where(m =>
                    m.Effect != MoveEffect.Metronome
                    && m.Name != "mirror-move"
                    && m.Name != "struggle"
                )
                .ToList();

            if (eligible.Count > 0)
                return ExecuteInner(eligible[_rng.Next(eligible.Count)]);
            return Task.CompletedTask;
        }

        // Mirror Move: re-run the foe's last move in full (through its own inner action, which records it
        // as this user's last move). Fails if the foe has no copyable move yet; only Struggle is excluded
        // (see above). Copying a lock-in move is valid Gen 1 — Bide starts a fresh commitment, and a
        // two-turn move (Fly) charges then auto-releases through the normal lock-in path on this user.
        if (!usingStruggle && attackToUse.Effect == MoveEffect.MirrorMove)
        {
            var last = Target.Battle.LastMoveUsed;
            if (last != null && last.Name != "struggle")
                return ExecuteInner(last);
            _emitter?.Emit(new MoveMissed(Source.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        var category = usingStruggle ? DamageCategory.Standard : attackToUse.DamageCategory;

        // Pre-damage gates (OHKO / accuracy+miss-effects / thaw / immunity / crash / Dream Eater — see
        // ResolvePreDamageGates). Halt ends the turn; otherwise JustThawed feeds the post-damage status step.
        var gate = ResolvePreDamageGates(attackToUse, category, usingStruggle, lockIn, lockCtx);
        if (!gate.Proceed)
            return Task.CompletedTask;
        bool justThawed = gate.JustThawed;

        // Snapshot the shield state before any damage lands (see the _targetShieldedAtImpact field).
        _targetShieldedAtImpact = Target.Battle.SubstituteHp > 0;

        // Reflect (physical) / Light Screen (special) double the defender's defensive stat while up
        // (ignored on a crit); computed once and passed into every damage call.
        int screenMult =
            (attackToUse.AttackType == AttackType.Physical && Target.Battle.HasReflect)
            || (attackToUse.AttackType == AttackType.Special && Target.Battle.HasLightScreen)
                ? _rules.ScreenDefenseMultiplier
                : 1;

        int damage = ResolveDamage(attackToUse, category, usingStruggle, screenMult);

        // Struggle recoil
        if (usingStruggle)
        {
            int recoil = _rules.CalculateStruggleRecoil(Source, damage);
            Source.Attributes.ReceiveDamage(recoil);
            _emitter?.Emit(new RecoilDamage(Source.Name, recoil, Source.Attributes.HP));
        }

        // Recharge next turn (Hyper Beam): only set when the move actually dealt damage
        if (!usingStruggle && attackToUse.Effect == MoveEffect.Recharge && damage > 0)
            Source.Battle.IsRecharging = true;

        if (!justThawed)
            TryApplyStatus(attackToUse);

        TryApplyStatEffect(attackToUse);
        TryApplyMoveEffect(attackToUse, damage);

        lockIn?.OnTurnEnd(lockCtx!); // confuse the user if this was the rampage's last turn

        return Task.CompletedTask;
    }

    // Outcome of the pre-damage gates. Proceed == false ends the turn — the gate already emitted the
    // miss / no-effect event and applied any miss-time side-effects (Self-Destruct faint, Jump Kick crash,
    // rampage tick). JustThawed feeds the post-damage status step (it skips status the turn we thawed).
    private readonly record struct PreDamageGateResult(bool Proceed, bool JustThawed)
    {
        public static readonly PreDamageGateResult Halt = new(false, false);

        public static PreDamageGateResult Continue(bool justThawed) => new(true, justThawed);
    }

    // The gauntlet a move clears before damage, in Gen 1 order: OHKO success → accuracy (with on-miss
    // Self-Destruct faint / Jump Kick crash / rampage tick) → freeze thaw → type-immunity wall → Jump Kick
    // crash on immunity → Dream Eater sleep precondition. Each failing gate emits its outcome and returns
    // Halt. Side-effects stay here (not on the result) since the gate owns the emitter/rules.
    private PreDamageGateResult ResolvePreDamageGates(
        Attack move,
        DamageCategory category,
        bool usingStruggle,
        ILockInMechanic? lockIn,
        LockInContext? lockCtx
    )
    {
        // OHKO *fails* (not misses) by the gen's success rule — Gen 1: fails if the target out-speeds the
        // user (the level check is Gen 2+). Gen-variable ⇒ on the seam. Independent of the accuracy roll.
        if (category == DamageCategory.OHKO && !_rules.OneHitKoSucceeds(Source, Target))
        {
            _emitter?.Emit(new MoveMissed(Source.Name, move.Name ?? ""));
            return PreDamageGateResult.Halt;
        }

        // Accuracy check — Struggle and NeverMisses moves always hit
        if (!usingStruggle && !move.NeverMisses)
        {
            int threshold = _rules.GetHitThreshold(
                move.Accuracy,
                Source.Battle.Stages.Accuracy,
                Target.Battle.Stages.Evasion
            );
            if (_rng.Next(_rules.AccuracyRollBound) >= threshold)
            {
                _emitter?.Emit(new MoveMissed(Source.Name, move.Name ?? ""));

                // Gen 1: Self-Destruct user faints even on miss
                if (category == DamageCategory.SelfDestruct)
                    Source.Attributes.ReceiveDamage(Source.Attributes.HP);

                // Gen 1: Jump Kick / Hi Jump Kick deal crash damage to the user on a miss
                if (move.Effect == MoveEffect.Crash)
                {
                    int crash = _rules.CalculateCrashDamage(Source);
                    Source.Attributes.ReceiveDamage(crash);
                    _emitter?.Emit(new CrashDamage(Source.Name, crash, Source.Attributes.HP));
                }

                lockIn?.OnTurnEnd(lockCtx!); // a missed turn still counts toward the rampage lock
                return PreDamageGateResult.Halt;
            }
        }

        // Thaw a frozen target if the move meets the generation's thaw criteria
        bool justThawed = false;
        if (Target.Battle.Status == StatusCondition.Freeze && _rules.CanThawFrozenTarget(move))
        {
            Target.Battle.Status = StatusCondition.None;
            _emitter?.Emit(new StatusCleared(Target.Name, StatusCondition.Freeze));
            justThawed = true;
        }

        // Gen 1: a target immune to the move's type takes nothing — including moves that bypass the normal
        // calc (fixed / level-based / OHKO / Super Fang) and pure-status moves (Thunder Wave is Electric ⇒
        // Ground is immune). Damaging Standard/Drain folds 0× into 0 damage, and Self-Destruct still
        // detonates the user — both excluded here.
        bool isPureStatusMove =
            category == DamageCategory.Standard && !usingStruggle && move.BaseDamage == 0;
        // Gen 1: a non-damaging move almost always IGNORES type immunity — Confuse Ray confuses a Normal-
        // type, Glare paralyses a Ghost, Growl / sleep / Disable land regardless of matchup. Only Thunder
        // Wave and Counter still consult the chart, and which do is gen-variable (Gen 2 makes status moves
        // respect immunity) ⇒ on IBattleRules. (Self-targeting moves never check the foe's type; Leech Seed
        // vs Grass and "Poison can't be poisoned" have their own immunity via CanBeLeechSeeded/CanReceiveStatus.)
        bool pureStatusChecksImmunity = _rules.PureStatusMoveChecksTypeImmunity(move);
        double typeImmunity = DamageCalculator.GetTypeEffectiveness(
            move.DamageType,
            Target.Type1,
            Target.Type2,
            _typeChart
        );
        if (
            typeImmunity == 0
            && (
                (isPureStatusMove && pureStatusChecksImmunity)
                || category
                    is DamageCategory.Fixed
                        or DamageCategory.LevelBased
                        or DamageCategory.OHKO
                        or DamageCategory.SuperFang
                        or DamageCategory.Psywave
            )
        )
        {
            _emitter?.Emit(new MoveHadNoEffect(Target.Name, move.Name ?? ""));
            return PreDamageGateResult.Halt;
        }

        // Gen 1: Jump Kick / Hi Jump Kick also crash on a type immunity (Fighting → Ghost 0×), not just an
        // accuracy miss. They're Standard-category so they slip past the immunity block above (which excludes
        // Standard) and would otherwise whiff harmlessly. Mirror the miss branch: announce no-effect, then crash.
        if (move.Effect == MoveEffect.Crash && typeImmunity == 0)
        {
            _emitter?.Emit(new MoveHadNoEffect(Target.Name, move.Name ?? ""));
            int crash = _rules.CalculateCrashDamage(Source);
            Source.Attributes.ReceiveDamage(crash);
            _emitter?.Emit(new CrashDamage(Source.Name, crash, Source.Attributes.HP));
            return PreDamageGateResult.Halt;
        }

        // Dream Eater only works on a sleeping target; else it fails (no damage, no heal). Invariant across
        // gens (a property of the move, not a rule) ⇒ inline, not on the seam. The 50% drain heal rides the
        // Drain category once sleep is confirmed. The failure is a *state* precondition (target awake), like
        // Counter with no damage to return — so it reuses MoveMissed, not the type-based MoveHadNoEffect.
        if (move.Effect == MoveEffect.DreamEater && Target.Battle.Status != StatusCondition.Sleep)
        {
            _emitter?.Emit(new MoveMissed(Source.Name, move.Name ?? ""));
            return PreDamageGateResult.Halt;
        }

        return PreDamageGateResult.Continue(justThawed);
    }

    // Resolves the move's damage by category and applies it via DealDamageToTarget (so the Substitute soak,
    // Counter recording and Bide accumulation stay centralized). Returns the total dealt — what ExecuteAsync
    // gates Struggle recoil, the Drain heal and the Hyper Beam recharge on. Runs after the gates clear.
    private int ResolveDamage(
        Attack move,
        DamageCategory category,
        bool usingStruggle,
        int screenMult
    )
    {
        int damage = 0;
        bool isCrit = false;

        switch (category)
        {
            case DamageCategory.Standard:
            case DamageCategory.Drain:
            {
                if (usingStruggle || move.BaseDamage > 0)
                {
                    double eff = DamageCalculator.GetTypeEffectiveness(
                        move.DamageType,
                        Target.Type1,
                        Target.Type2,
                        _typeChart
                    );

                    // Multi-hit (Double Slap, …): accuracy was rolled once above; each of the 2–5 strikes
                    // rolls its own crit + variance and stops if the target faints (a normal move = the
                    // hits==1 path, same output). Fixed-count movers (Double Kick = 2) carry the count as
                    // data; variable movers leave it null and draw the 2–5 count from the gen rules.
                    bool isMultiHit = !usingStruggle && move.Effect == MoveEffect.MultiHit;
                    int hits = isMultiHit ? (move.MultiHitCount ?? _rules.RollMultiHitCount()) : 1;

                    int landed = 0;
                    bool dealtRealDamage = false;
                    for (int i = 0; i < hits && Target.IsAlive(); i++)
                    {
                        int hitDamage = DamageCalculator.CalculateDamage(
                            Source,
                            Target,
                            move,
                            _typeChart,
                            _rules,
                            out isCrit,
                            _rng,
                            screenDefenseMultiplier: screenMult
                        );
                        if (DealDamageToTarget(hitDamage, eff, isCrit, move.DamageType))
                            dealtRealDamage = true;
                        damage += hitDamage; // accumulated total gates drain/recoil/recharge below
                        landed++;
                    }

                    if (isMultiHit)
                        _emitter?.Emit(new MultiHitCompleted(landed));

                    // Rage: a raging creature that's hit gains Attack stage(s) — once per connecting attack
                    // (the Counter boundary), not per multi-hit strike, and only when the hit reached the
                    // creature itself (a Substitute soak doesn't enrage it). Reuses StatStageChanged.
                    if (dealtRealDamage && Target.Battle.IsRaging && Target.IsAlive())
                    {
                        int newStage = ApplyStageChange(
                            Target,
                            StageStat.Attack,
                            _rules.RageAttackStagesPerHit
                        );
                        _emitter?.Emit(
                            new StatStageChanged(
                                Target.Name,
                                StageStat.Attack.ToString(),
                                _rules.RageAttackStagesPerHit,
                                newStage
                            )
                        );
                    }

                    if (category == DamageCategory.Drain && damage > 0)
                    {
                        int heal = Math.Max(1, damage * move.DrainPercent / 100);
                        Source.Attributes.ReceiveHealing(heal);
                        _emitter?.Emit(new DrainHealed(Source.Name, heal, Source.Attributes.HP));
                    }
                }
                break;
            }

            case DamageCategory.Fixed:
                damage = move.FixedDamageValue ?? 1;
                DealDamageToTarget(damage, 1.0, false, move.DamageType);
                break;

            case DamageCategory.LevelBased:
                damage = DamageCalculator.CalculateLevelBasedDamage(Source);
                DealDamageToTarget(damage, 1.0, false, move.DamageType);
                break;

            case DamageCategory.OHKO:
                // Against a Substitute the OHKO just breaks the decoy (the soak caps at the sub's HP);
                // otherwise it removes the target's full current HP.
                damage = Target.Attributes.HP;
                DealDamageToTarget(damage, 1.0, false, move.DamageType);
                break;

            case DamageCategory.SelfDestruct:
            {
                // Gen 1: the target's Defense is halved before the calc, making Explosion/Self-Destruct much
                // stronger. The divisor is gen-variable (gone in Gen 5+) ⇒ from the seam, passed into the
                // calculator — no longer mutate-and-restore the creature's real stats.
                double eff = DamageCalculator.GetTypeEffectiveness(
                    move.DamageType,
                    Target.Type1,
                    Target.Type2,
                    _typeChart
                );
                damage = DamageCalculator.CalculateDamage(
                    Source,
                    Target,
                    move,
                    _typeChart,
                    _rules,
                    out isCrit,
                    _rng,
                    defenseDivisor: _rules.SelfDestructDefenseDivisor,
                    screenDefenseMultiplier: screenMult
                );

                DealDamageToTarget(damage, eff, isCrit, move.DamageType);

                // User faints unconditionally (already handled miss → faint above)
                Source.Attributes.ReceiveDamage(Source.Attributes.HP);
                break;
            }

            case DamageCategory.SuperFang:
                damage = DamageCalculator.CalculateSuperFangDamage(Target);
                DealDamageToTarget(damage, 1.0, false, move.DamageType);
                break;

            case DamageCategory.Psywave:
                // Gen 1: a random 1..floor(1.5×level), ignoring Attack/Defense, type effectiveness,
                // STAB and crits. The magnitude is gen-variable, so it comes from the rules seam.
                damage = _rules.RollPsywaveDamage(Source, _rng);
                DealDamageToTarget(damage, 1.0, false, move.DamageType);
                break;
        }

        return damage;
    }

    // Runs another move in full as this turn's action (Metronome / Mirror Move). A fresh AttackAction on a
    // temporary wrapper leaves the creature's own PP/moveset untouched and records it as the user's last move.
    private Task ExecuteInner(Attack move) =>
        new AttackAction(
            Source,
            Target,
            new PokemonAttack(move),
            _typeChart,
            _rules,
            _emitter,
            _movePool,
            _rng,
            _battleEscapable
        ).ExecuteAsync();

    // Bide stores every hit the committed user takes — any damage category — to unleash 2× on release.
    private void AccumulateBideDamage(int dmg)
    {
        if (Target.Battle.BideTurnsRemaining > 0)
            Target.Battle.BideDamageAccumulated += dmg;
    }

    // Applies <paramref name="dmg"/> to the Target, honoring an active Substitute. Gen 1: while a Substitute
    // stands the decoy soaks the whole hit — the user's HP is untouched and overflow is lost (it does NOT
    // carry through, even from an OHKO). Returns true when the real creature took the hit (so a caller can
    // run on-real-hit follow-ups like Rage), false when the sub absorbed it. Every damage category routes
    // through here so the soak can't be missed by one branch (the recurring "hook on only Standard" defect).
    //
    // <paramref name="counterableType"/> is the move's type when the hit is recordable for the target's
    // Counter (read back as 2× the last Normal/Fighting damage). Recorded centrally here, gated on real
    // damage, so every damaging category is counterable through one path; callers that must not be (Bide) pass null.
    private bool DealDamageToTarget(
        int dmg,
        double effectiveness,
        bool isCrit,
        DamageType? counterableType = null
    )
    {
        if (Target.Battle.SubstituteHp > 0)
        {
            Target.Battle.SubstituteHp = Math.Max(0, Target.Battle.SubstituteHp - dmg);
            if (Target.Battle.SubstituteHp == 0)
                _emitter?.Emit(new SubstituteFaded(Target.Name));
            else
                _emitter?.Emit(new SubstituteAbsorbedHit(Target.Name, Target.Battle.SubstituteHp));
            return false;
        }

        Target.Attributes.ReceiveDamage(dmg);
        AccumulateBideDamage(dmg);

        // Record for the target's Counter — the last hit that actually connected with the creature (a
        // hit soaked by a Substitute returned above, so it never reaches here and doesn't update this).
        if (counterableType is { } type)
        {
            Target.Battle.LastDamageTaken = dmg;
            Target.Battle.LastDamageType = type;
        }

        _emitter?.Emit(
            new DamageDealt(
                Target.Name,
                dmg,
                effectiveness,
                Target.Attributes.HP,
                Target.Attributes.MaxHP,
                isCrit
            )
        );
        return true;
    }

    // The shield flag consumed by the foe-directed secondaries (status / stat-drop / confusion);
    // see the _targetShieldedAtImpact field for the Gen 1 rationale.
    private bool TargetShieldedBySubstitute => _targetShieldedAtImpact;

    private void TryApplyStatus(Attack attack)
    {
        if (attack.StatusEffect == StatusCondition.None)
            return;
        if (Target.Battle.Status != StatusCondition.None)
            return;
        if (!Target.IsAlive())
            return;
        // Gen 1: a Substitute shields the user from the foe's status (the hit, if any, struck the decoy).
        if (TargetShieldedBySubstitute)
            return;

        // Gen 1 type immunity (Poison can't be poisoned, Fire can't be burned, Body Slam can't
        // paralyze a Normal-type). For a pure status move this is "it doesn't affect …"; on a
        // damaging move with a secondary status, the hit already landed, so just skip silently.
        if (!_rules.CanReceiveStatus(Target, attack.StatusEffect, attack.DamageType))
        {
            if (attack.BaseDamage == 0)
                _emitter?.Emit(new MoveHadNoEffect(Target.Name, attack.Name ?? ""));
            return;
        }

        int chance = _rules.GetSecondaryEffectChance(attack, SecondaryEffectKind.Status);
        if (!_rules.SecondaryHits(chance, _rng))
            return;

        Target.Battle.Status = attack.StatusEffect;

        if (attack.StatusEffect == StatusCondition.Sleep)
            Target.Battle.SleepTurns = _rules.RollSleepTurns();

        _emitter?.Emit(new StatusApplied(Target.Name, attack.StatusEffect));
    }

    private void TryApplyStatEffect(Attack attack)
    {
        var se = attack.StatEffect;
        if (se == null)
            return;
        if (!Target.IsAlive() && se.Target == StageTarget.Foe)
            return;

        Creature affected = se.Target == StageTarget.Self ? Source : Target;

        // Gen 1: a Substitute shields the user from the foe's stat changes while it stands. A
        // self-targeting stat change (the user's own buff) is unaffected.
        if (se.Target == StageTarget.Foe && TargetShieldedBySubstitute)
            return;

        // Gen 1 Mist: the opponent cannot lower the Mist-holder's stats. Self-inflicted drops
        // (and any raise) are unaffected.
        if (se.Target == StageTarget.Foe && se.Delta < 0 && affected.Battle.HasMist)
        {
            _emitter?.Emit(new StatDropBlocked(affected.Name));
            return;
        }

        // Chance comes from the rules seam (Gen 1 reads the move's single chance column for every
        // secondary kind) rather than the StatEffectChance column directly — keeps the call site
        // generation-agnostic, like the status/flinch/confuse secondaries.
        int chance = _rules.GetSecondaryEffectChance(attack, SecondaryEffectKind.StatStage);
        if (!_rules.SecondaryHits(chance, _rng))
            return;

        int newStage = ApplyStageChange(affected, se.Stat, se.Delta);
        _emitter?.Emit(new StatStageChanged(affected.Name, se.Stat.ToString(), se.Delta, newStage));
    }

    // Post-damage move effects (Haze, Counter, Reflect, Transform, Rest, Substitute …) live behind
    // IMoveEffect in the MoveEffects registry, keyed by MoveEffect (mirroring LockInMechanics.For). One
    // runs per move; the context's DealDamage delegate keeps the soak / Bide / Counter recording centralized.
    private void TryApplyMoveEffect(Attack attack, int damage)
    {
        var effect = MoveEffects.For(attack.Effect);
        if (effect is null)
            return;

        effect.Apply(
            new MoveEffectContext
            {
                Source = Source,
                Target = Target,
                Attack = attack,
                SelectedMove = _selectedMove,
                Damage = damage,
                Rules = _rules,
                Rng = _rng,
                Emitter = _emitter,
                TargetShieldedBySubstitute = TargetShieldedBySubstitute,
                BattleEscapable = _battleEscapable,
                DealDamage = DealDamageToTarget,
            }
        );
    }

    private static int ApplyStageChange(Creature creature, StageStat stat, int delta) =>
        stat switch
        {
            StageStat.Attack => After(
                () => creature.Battle.Stages.RaiseAttack(delta),
                () => creature.Battle.Stages.Attack
            ),
            StageStat.Defense => After(
                () => creature.Battle.Stages.RaiseDefense(delta),
                () => creature.Battle.Stages.Defense
            ),
            StageStat.Special => After(
                () => creature.Battle.Stages.RaiseSpecial(delta),
                () => creature.Battle.Stages.Special
            ),
            StageStat.Speed => After(
                () => creature.Battle.Stages.RaiseSpeed(delta),
                () => creature.Battle.Stages.Speed
            ),
            StageStat.Accuracy => After(
                () => creature.Battle.Stages.RaiseAccuracy(delta),
                () => creature.Battle.Stages.Accuracy
            ),
            StageStat.Evasion => After(
                () => creature.Battle.Stages.RaiseEvasion(delta),
                () => creature.Battle.Stages.Evasion
            ),
            _ => 0,
        };

    private static int After(Action mutate, Func<int> read)
    {
        mutate();
        return read();
    }
}
