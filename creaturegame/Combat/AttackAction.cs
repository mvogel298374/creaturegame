using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public class AttackAction : IBattleAction
{
    public Creature Source { get; }

    // Reassignable by Battle's post-switch retarget (see Retarget below); never mid-execution otherwise.
    public Creature Target { get; private set; }
    public int Priority { get; }
    private readonly ITypeChart _typeChart;
    private readonly IBattleRules _rules;
    private readonly IBattleEventEmitter? _emitter;
    private readonly IReadOnlyList<Attack> _movePool;
    private readonly IRandomSource _rng;

    // Whether this battle can be fled — gates Roar/Whirlwind (ForceFlee). True for a plain wild battle / the
    // legacy chain; the run layer sets it false for the trainer-analog encounters (Elite/Boss).
    private readonly bool _battleEscapable;

    private readonly BattleSide _side;

    // Whether the Target had a Substitute up when this move connected — snapshotted pre-damage so a
    // secondary is still shielded on the very hit that breaks the decoy (Gen 1: that hit struck the sub).
    private bool _targetShieldedAtImpact;

    private bool _brokeSubstitute;

    // Null means Struggle — Battle passes null when the source has no selectable move
    // (out of PP, or its only move is Disabled), bypassing IBattleInput.
    private readonly PokemonAttack? _selectedMove;

    internal PokemonAttack? SelectedMove => _selectedMove;

    public AttackAction(
        Creature source,
        Creature target,
        PokemonAttack? selectedMove,
        ITypeChart typeChart,
        IBattleRules? rules = null,
        IBattleEventEmitter? emitter = null,
        IReadOnlyList<Attack>? movePool = null,
        IRandomSource? rng = null,
        bool battleEscapable = true,
        BattleSide side = BattleSide.Player
    )
    {
        Source = source;
        _side = side;
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

    /// <summary>
    /// Repoints this action at a new <paramref name="target"/>. Called by <see cref="Battle"/> on the enemy's
    /// still-queued move right after the player's voluntary switch executes this turn — so a slower (or even
    /// priority) enemy move lands on the creature that came in, not the one that just left the field. A no-op
    /// for the switch's own turn ordering (the switch always resolves first), it only fixes the retarget.
    /// </summary>
    internal void Retarget(Creature target) => Target = target;

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive())
            return Task.CompletedTask;

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
        var commit = lockIn?.OnCommit(lockCtx!).Flow ?? LockInFlow.Proceed;
        if (commit == LockInFlow.Halt)
            return Task.CompletedTask;

        if (commit != LockInFlow.ProceedUnannounced)
            _emitter?.Emit(new MoveUsed(Source.Name, Source.Id, attackToUse.Name ?? ""));

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
                // Both route through DealDamageToTarget, so a Substitute soaks them; Bide's unleash is typeless.
                if (release.ReplaysHit)
                    ReplayHit(attackToUse, release.Damage);
                else if (release.Damage > 0)
                    DealDamageToTarget(release.Damage, 1.0, false);
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
            _emitter?.Emit(new MoveMissed(Source.Name, Source.Id, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        var category = usingStruggle ? DamageCategory.Standard : attackToUse.DamageCategory;
        int screenMult = DamageCalculator.ScreenMultiplier(Target, attackToUse, _rules);
        double effectiveness = DamageCalculator.GetTypeEffectiveness(
            attackToUse.DamageType,
            Target.Type1,
            Target.Type2,
            _typeChart
        );
        bool immune =
            effectiveness == 0 && ChecksTypeImmunity(attackToUse, category, usingStruggle);

        if (SleepsRechargingTarget(attackToUse, category, usingStruggle))
            return Task.CompletedTask;

        bool checksBeforeHitTest =
            _rules.PrimaryEffectChecksPrecedeHitTest
            && IsStatusMove(attackToUse, category, usingStruggle);
        if (checksBeforeHitTest && StatusMoveStopsBeforeHitTest(attackToUse))
            return Task.CompletedTask;

        if (
            immune
            && IsStatusMove(attackToUse, category, usingStruggle)
            && _rules.StatusMoveImmunityBeforeAccuracy
        )
        {
            _emitter?.Emit(new MoveHadNoEffect(Target.Name, Target.Id, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        if (
            IsFoeStatDown(attackToUse)
            && attackToUse.BaseDamage == 0
            && HandicapFails(attackToUse, isSecondary: false)
        )
        {
            _emitter?.Emit(new MoveFailed());
            return Task.CompletedTask;
        }

        if (
            checksBeforeHitTest
            && IsFoeStatDown(attackToUse)
            && StatDownStopsBeforeHitTest(attackToUse)
        )
            return Task.CompletedTask;

        // Before the hit test, so a trap move that misses still clears the recharge.
        if (attackToUse.Effect == MoveEffect.Binding && !isLockedInContinuation)
            StatusResolver.ClearRechargeOn(Target, RechargeClearingInfliction.Trap, _rules);

        // Roll order per IBattleRules.HitRollOrder — see GENERATION_SEAMS.md §2.
        Strike? strike;
        if (_rules.HitRollOrder == HitRollOrder.CritAndDamageThenAccuracy)
        {
            if (
                !TryRollBeforeHitTest(
                    attackToUse,
                    category,
                    usingStruggle,
                    immune,
                    screenMult,
                    out strike
                )
            )
                return Task.CompletedTask;
            bool typeZeroed = TypeZeroes(strike, effectiveness);
            if (
                !Connects(attackToUse, category, usingStruggle, immune, typeZeroed, lockIn, lockCtx)
            )
                return Task.CompletedTask;
        }
        else
        {
            if (category == DamageCategory.OHKO && OhkoFails(attackToUse))
                return Task.CompletedTask;
            if (
                !Connects(
                    attackToUse,
                    category,
                    usingStruggle,
                    immune,
                    typeZeroed: false,
                    lockIn,
                    lockCtx
                )
            )
                return Task.CompletedTask;
            strike = RollsCritAndDamage(attackToUse, category, usingStruggle)
                ? RollStrike(attackToUse, category, usingStruggle, screenMult)
                : null;
            if (TypeZeroes(strike, effectiveness))
            {
                EndTypeZeroedHit(attackToUse, category, lockIn, lockCtx);
                return Task.CompletedTask;
            }
        }

        // Snapshot the shield state before any damage lands (see the _targetShieldedAtImpact field).
        _targetShieldedAtImpact = Target.Battle.SubstituteHp > 0;
        _brokeSubstitute = false;

        int damage = ResolveDamage(
            attackToUse,
            category,
            usingStruggle,
            effectiveness,
            strike,
            screenMult
        );

        // Struggle recoil
        if (usingStruggle && !BreakCancels(MoveEffect.Recoil, category))
        {
            int recoil = _rules.CalculateStruggleRecoil(Source, damage);
            Source.Attributes.ReceiveDamage(recoil);
            _emitter?.Emit(new RecoilDamage(Source.Name, Source.Id, recoil, Source.Attributes.HP));
        }

        // Recharge (Hyper Beam) only when damage landed AND not a KO — IBattleRules.FaintEndsTurnImmediately;
        // TODO_ARCHIVE.md → "End-of-turn residual … fired even after a same-turn faint" for why the KO check
        // matters (a forced switch-in reuses the same EnemyCreature instance, never reset mid-battle).
        if (
            !usingStruggle
            && attackToUse.Effect == MoveEffect.Recharge
            && damage > 0
            && (!_rules.FaintEndsTurnImmediately || Target.IsAlive())
            && !BreakCancels(attackToUse.Effect, category)
        )
            Source.Battle.IsRecharging = true;

        bool justThawed = TryThawTarget(attackToUse);
        if (!justThawed)
            TryApplyStatus(attackToUse);

        TryApplyStatEffect(attackToUse);
        if (!BreakCancels(attackToUse.Effect, category))
        {
            TryApplyMoveEffect(attackToUse, damage);
            lockIn?.OnConnect(lockCtx!);
        }

        lockIn?.OnTurnEnd(lockCtx!); // confuse the user if this was the rampage's last turn

        return Task.CompletedTask;
    }

    private readonly record struct Strike(int Hits, int Damage, bool IsCrit);

    private static bool RollsCritAndDamage(
        Attack move,
        DamageCategory category,
        bool usingStruggle
    ) =>
        category is DamageCategory.Standard or DamageCategory.Drain or DamageCategory.SelfDestruct
        && (usingStruggle || move.BaseDamage > 0);

    private static bool IsStatusMove(Attack move, DamageCategory category, bool usingStruggle) =>
        category == DamageCategory.Standard
        && !usingStruggle
        && move.BaseDamage == 0
        && move.Effect != MoveEffect.Counter;

    // Whether a 0× matchup stops this move. Counter's rule is on the seam even though it is a 0-power move.
    private bool ChecksTypeImmunity(Attack move, DamageCategory category, bool usingStruggle)
    {
        if (move.Effect == MoveEffect.Counter)
            return _rules.FixedDamageChecksTypeImmunity;
        return category switch
        {
            DamageCategory.Standard or DamageCategory.Drain => usingStruggle
                || move.BaseDamage > 0
                || _rules.PureStatusMoveChecksTypeImmunity(move),
            DamageCategory.SelfDestruct or DamageCategory.OHKO => true,
            DamageCategory.Fixed
            or DamageCategory.LevelBased
            or DamageCategory.SuperFang
            or DamageCategory.Psywave => _rules.FixedDamageChecksTypeImmunity,
            _ => false,
        };
    }

    // The rolls that precede the hit test under CritAndDamageThenAccuracy. False ends the turn.
    private bool TryRollBeforeHitTest(
        Attack move,
        DamageCategory category,
        bool usingStruggle,
        bool immune,
        int screenMult,
        out Strike? strike
    )
    {
        strike = null;
        if (RollsCritAndDamage(move, category, usingStruggle))
        {
            strike = RollStrike(move, category, usingStruggle, screenMult);
            return true;
        }

        if (category == DamageCategory.OHKO)
        {
            _rng.NextDouble(); // the crit test runs, but its result is discarded
            if (OhkoFails(move))
                return false;
            if (!immune || !_rules.DamageRollSkippedBelowTwo)
                _rules.RollDamageVariance(); // drawn on the OHKO damage, which it cannot change
            return true;
        }

        if (move.Effect == MoveEffect.Counter)
        {
            _rng.NextDouble(); // see GEN_DIFFERENCES.md → Roll Order (Counter)
            if (!CounterEffect.Qualifies(Source, Target, _rules))
            {
                _emitter?.Emit(new MoveMissed(Source.Name, Source.Id, move.Name ?? ""));
                return false;
            }
            return true;
        }
        return true;
    }

    private bool OhkoFails(Attack move)
    {
        if (_rules.OneHitKoSucceeds(Source, Target))
            return false;
        _emitter?.Emit(
            MoveFailureEvents.For(
                _rules.OneHitKoFailureAnnouncement,
                Source,
                Target,
                move.Name ?? "",
                UnaffectedCause.OneHitKo
            )
        );
        return true;
    }

    private Strike RollStrike(
        Attack move,
        DamageCategory category,
        bool usingStruggle,
        int screenMult
    )
    {
        int hits =
            !usingStruggle && move.Effect == MoveEffect.MultiHit
                ? move.MultiHitCount ?? _rules.RollMultiHitCount()
                : 1;
        int damage = RollCritAndDamage(move, category, screenMult, out bool isCrit);
        return new Strike(hits, damage, isCrit);
    }

    private int RollCritAndDamage(
        Attack move,
        DamageCategory category,
        int screenMult,
        out bool isCrit
    ) =>
        DamageCalculator.CalculateDamage(
            Source,
            Target,
            move,
            _typeChart,
            _rules,
            out isCrit,
            _rng,
            defenseDivisor: category == DamageCategory.SelfDestruct
                ? _rules.SelfDestructDefenseDivisor
                : 1,
            screenDefenseMultiplier: screenMult
        );

    // A non-immune hit the type multiplier rounded to 0; GEN_DIFFERENCES.md → Type-reduced zero damage.
    private bool TypeZeroes(Strike? strike, double effectiveness) =>
        _rules.TypeReducedZeroDamageMisses
        && strike is { Damage: 0 }
        && effectiveness is > 0 and < 1.0;

    // The type-zeroed miss once the accuracy roll has passed: the miss line, and a binding move still locks.
    private void EndTypeZeroedHit(
        Attack move,
        DamageCategory category,
        ILockInMechanic? lockIn,
        LockInContext? lockCtx
    )
    {
        _emitter?.Emit(new MoveMissed(Source.Name, Source.Id, move.Name ?? ""));
        LockDespiteTypeMiss(move, lockCtx);
        ApplyFailedHitSideEffects(move, category, crashes: true);
        lockIn?.OnTurnEnd(lockCtx!);
    }

    private void LockDespiteTypeMiss(Attack move, LockInContext? lockCtx)
    {
        if (
            move.Effect != MoveEffect.Binding
            || !_rules.BindingLockSurvivesTypeMiss
            || lockCtx is not { IsContinuation: false }
            || Target.Battle.BindingTurnsRemaining > 0
            || !Target.IsAlive()
        )
            return;
        BindingEffect.Start(Source, Target, lockCtx.Move, 0, _rules, _emitter);
    }

    // The hit test; false ends the turn after emitting the miss / no-effect outcome.
    private bool Connects(
        Attack move,
        DamageCategory category,
        bool usingStruggle,
        bool immune,
        bool typeZeroed,
        ILockInMechanic? lockIn,
        LockInContext? lockCtx
    )
    {
        if (move.Effect == MoveEffect.DreamEater && Target.Battle.Status != StatusCondition.Sleep)
        {
            _emitter?.Emit(new MoveMissed(Source.Name, Source.Id, move.Name ?? ""));
            return false;
        }

        var outOfReach = Target.Battle.SemiInvulnerability;
        var reach =
            outOfReach == SemiInvulnerability.None
                ? SemiInvulnerableHit.Lands
                : _rules.HitOnSemiInvulnerable(move, outOfReach);
        if (reach == SemiInvulnerableHit.Fails)
        {
            _emitter?.Emit(new MoveFailed());
            lockIn?.OnTurnEnd(lockCtx!);
            return false;
        }

        bool dodged = reach == SemiInvulnerableHit.Misses;
        bool skipsAccuracyRoll =
            dodged
            || usingStruggle
            || move.NeverMisses
            || (move.Effect == MoveEffect.ForceFlee && _rules.ForceFleeSkipsAccuracy)
            || (_rules.XAccuracyBypassesAccuracyCheck && Source.Battle.UsingXAccuracy);
        bool missed =
            dodged
            || (
                !skipsAccuracyRoll
                && _rng.Next(_rules.AccuracyRollBound)
                    >= _rules.GetHitThreshold(
                        move.Accuracy,
                        Source.Battle.Stages.Accuracy,
                        Target.Battle.Stages.Evasion
                    )
            );
        bool immunityOutranksMiss =
            _rules.HitRollOrder == HitRollOrder.CritAndDamageThenAccuracy
            && !IsStatusMove(move, category, usingStruggle);

        if (immune && (!missed || immunityOutranksMiss))
        {
            _emitter?.Emit(new MoveHadNoEffect(Target.Name, Target.Id, move.Name ?? ""));
            if (!missed)
                LockDespiteTypeMiss(move, lockCtx);
            ApplyFailedHitSideEffects(move, category, crashes: _rules.CrashOnTypeImmunity);
            lockIn?.OnTurnEnd(lockCtx!);
            return false;
        }

        if (typeZeroed && !missed)
        {
            EndTypeZeroedHit(move, category, lockIn, lockCtx);
            return false;
        }

        if (missed)
        {
            bool isStatusMove = IsStatusMove(move, category, usingStruggle);
            _emitter?.Emit(
                isStatusMove && move.StatusEffect != StatusCondition.None
                    ? StatusMoveFailure(_rules.StatusMoveMissAnnouncement, move)
                : isStatusMove
                && (IsFoeStatDown(move) || move.Effect is MoveEffect.Confuse or MoveEffect.Disable)
                    ? StatusMoveFailure(_rules.PrimaryEffectFailureAnnouncement, move)
                : new MoveMissed(Source.Name, Source.Id, move.Name ?? "")
            );
            ApplyFailedHitSideEffects(move, category, crashes: true);
            lockIn?.OnTurnEnd(lockCtx!);
            return false;
        }

        return true;
    }

    // Skips every check and lands even over a status. See GENERATION_SEAMS.md §2 (Pre-move checks).
    private bool SleepsRechargingTarget(Attack move, DamageCategory category, bool usingStruggle)
    {
        if (
            !_rules.SleepMoveIgnoresChecksOnRechargingTarget
            || !Target.Battle.IsRecharging
            || move.StatusEffect != StatusCondition.Sleep
            || !IsStatusMove(move, category, usingStruggle)
        )
            return false;
        InflictStatus(move);
        return true;
    }

    // True when the move ended here. See GENERATION_SEAMS.md §2 (Pre-hit checks).
    private bool StatusMoveStopsBeforeHitTest(Attack move)
    {
        if (move.Effect == MoveEffect.Confuse)
        {
            if (Target.Battle.SubstituteHp == 0 || !_rules.SubstituteStopsPureStatusMove(move))
                return false;
            _emitter?.Emit(StatusMoveFailure(_rules.PrimaryEffectFailureAnnouncement, move));
            return true;
        }
        if (move.StatusEffect == StatusCondition.None)
            return false;

        if (Target.Battle.SubstituteHp > 0 && _rules.SubstituteStopsPureStatusMove(move))
        {
            _emitter?.Emit(StatusMoveFailure(_rules.StatusMoveNoOpAnnouncement, move));
            return true;
        }
        if (Target.Battle.Status != StatusCondition.None)
        {
            AnnounceAlreadyStatused(move);
            return true;
        }
        if (_rules.PureStatusMoveCanAffect(Target, move.StatusEffect))
            return false;
        _emitter?.Emit(StatusMoveFailure(_rules.StatusMoveNoOpAnnouncement, move));
        return true;
    }

    private bool StatDownStopsBeforeHitTest(Attack move)
    {
        if (Target.Battle.SubstituteHp > 0 && _rules.SubstituteStopsPureStatusMove(move))
        {
            _emitter?.Emit(StatusMoveFailure(_rules.PrimaryEffectFailureAnnouncement, move));
            return true;
        }
        if (!Target.Battle.HasMist || !_rules.MistStopsStatDrop(isSecondary: false))
            return false;
        _emitter?.Emit(MistBlockLine(Target));
        return true;
    }

    private BattleEvent MistBlockLine(Creature holder) =>
        _rules.MistBlockAnnouncement switch
        {
            MistBlockAnnouncement.Failed => new MoveFailed(),
            MistBlockAnnouncement.ProtectedByMist => new StatDropBlocked(holder.Name, holder.Id),
            _ => throw new ArgumentOutOfRangeException(
                nameof(_rules.MistBlockAnnouncement),
                _rules.MistBlockAnnouncement,
                null
            ),
        };

    private void AnnounceAlreadyStatused(Attack move)
    {
        if (
            move.StatusEffect == StatusCondition.Sleep
            && Target.Battle.Status == StatusCondition.Sleep
        )
            _emitter?.Emit(new AlreadyAsleep(Target.Name, Target.Id));
        else
            _emitter?.Emit(StatusMoveFailure(_rules.StatusMoveNoOpAnnouncement, move));
    }

    private BattleEvent StatusMoveFailure(MoveFailureAnnouncement announcement, Attack move) =>
        MoveFailureEvents.For(
            announcement,
            Source,
            Target,
            move.Name ?? "",
            UnaffectedCause.StatusMove
        );

    private void ApplyFailedHitSideEffects(Attack move, DamageCategory category, bool crashes)
    {
        if (category == DamageCategory.SelfDestruct)
            Source.Attributes.ReceiveDamage(Source.Attributes.HP);

        if (crashes && move.Effect == MoveEffect.Crash)
        {
            int crash = _rules.CalculateCrashDamage(Source);
            SelfInflictedDamage.Apply(
                Source,
                Target,
                crash,
                _rules,
                _emitter,
                hp => new CrashDamage(Source.Name, Source.Id, crash, hp)
            );
        }
    }

    // A thaw is a side effect of the hit, so it follows the damage and needs a surviving target.
    private bool TryThawTarget(Attack move)
    {
        if (
            !Target.IsAlive()
            || Target.Battle.Status != StatusCondition.Freeze
            || !_rules.CanThawFrozenTarget(move)
            || (TargetShieldedBySubstitute && _rules.SubstituteStopsThaw)
        )
            return false;

        Target.Battle.Status = StatusCondition.None;
        _emitter?.Emit(new StatusCleared(Target.Name, Target.Id, StatusCondition.Freeze));
        return true;
    }

    // Every category deals through DealDamageToTarget; returns the total dealt.
    private int ResolveDamage(
        Attack move,
        DamageCategory category,
        bool usingStruggle,
        double effectiveness,
        Strike? strike,
        int screenMult
    )
    {
        int damage = 0;

        // Snapshot before any damage lands (OHKO / Super Fang read the target's current HP) — rides on this
        // attack's DamageDealt events as the Dev Mode range (see _hitRange).
        _hitRange = DamageCalculator.EstimateRange(Source, Target, move, _typeChart, _rules, _side);

        switch (category)
        {
            case DamageCategory.Standard:
            case DamageCategory.Drain:
            {
                if (strike is { } first)
                {
                    bool isMultiHit = !usingStruggle && move.Effect == MoveEffect.MultiHit;
                    int landed = 0;
                    bool dealtRealDamage = false;
                    bool cutShort = false;
                    for (int i = 0; i < first.Hits && Target.IsAlive() && !cutShort; i++)
                    {
                        int hitDamage = first.Damage;
                        bool isCrit = first.IsCrit;
                        if (i > 0 && !_rules.MultiHitRepeatsFirstStrike)
                            hitDamage = RollCritAndDamage(move, category, screenMult, out isCrit);
                        _laterStrike = i > 0;
                        if (DealDamageToTarget(hitDamage, effectiveness, isCrit, move.DamageType))
                            dealtRealDamage = true;
                        damage += hitDamage; // accumulated total gates drain/recoil/recharge below
                        landed++;
                        cutShort = i == 0 && isMultiHit && BreakCancels(move.Effect, category);
                    }

                    if (isMultiHit && !cutShort)
                        _emitter?.Emit(new MultiHitCompleted(landed));

                    // Once per connecting attack, not per multi-hit strike; a Substitute soak doesn't enrage.
                    if (dealtRealDamage)
                        BuildRage();

                    if (
                        category == DamageCategory.Drain
                        && damage > 0
                        && !BreakCancels(move.Effect, category)
                    )
                    {
                        int heal = Math.Max(1, damage * move.DrainPercent / 100);
                        Source.Attributes.ReceiveHealing(heal);
                        _emitter?.Emit(
                            new DrainHealed(Source.Name, Source.Id, heal, Source.Attributes.HP)
                        );
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
                damage =
                    Target.Battle.SubstituteHp > 0
                        ? _rules.OhkoDamageToSubstitute
                        : Target.Attributes.HP;
                _ohkoStrike = true;
                DealDamageToTarget(damage, effectiveness, false, move.DamageType);
                break;

            case DamageCategory.SelfDestruct:
                if (strike is { } blast)
                {
                    damage = blast.Damage;
                    DealDamageToTarget(damage, effectiveness, blast.IsCrit, move.DamageType);
                }
                if (!BreakCancels(move.Effect, category))
                    Source.Attributes.ReceiveDamage(Source.Attributes.HP);
                break;

            case DamageCategory.SuperFang:
                damage = DamageCalculator.CalculateSuperFangDamage(Target);
                DealDamageToTarget(damage, 1.0, false, move.DamageType);
                break;

            case DamageCategory.Psywave:
                damage = _rules.RollPsywaveDamage(Source, _side, _rng);
                DealDamageToTarget(damage, 1.0, false, move.DamageType);
                break;
        }

        return damage;
    }

    private void BuildRage()
    {
        if (!Target.Battle.IsRaging || !Target.IsAlive())
            return;
        int newStage = ApplyStageChange(Target, StageStat.Attack, _rules.RageAttackStagesPerHit);
        _emitter?.Emit(
            new StatStageChanged(
                Target.Name,
                Target.Id,
                StageStat.Attack.ToString(),
                _rules.RageAttackStagesPerHit,
                newStage
            )
        );
    }

    // A binding continuation: no rolls, no crit or effectiveness line; GEN_DIFFERENCES.md → Binding.
    private void ReplayHit(Attack move, int damage)
    {
        _hitRange = DamageRange.Exact(damage);
        if (DealDamageToTarget(damage, 1.0, false, move.DamageType))
            BuildRage();
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
            _battleEscapable,
            _side
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
    // The current attack's non-crit damage range, stamped onto its DamageDealt events (Dev Mode combat log).
    // Set once at the top of ResolveDamage; a field rather than a parameter so every DealDamageToTarget call
    // site (one per damage category) carries it without a signature change.
    private DamageRange? _hitRange;

    // Any multi-hit strike after the first; a field for the same reason as _hitRange.
    private bool _laterStrike;

    private bool _ohkoStrike;

    private bool DealDamageToTarget(
        int dmg,
        double effectiveness,
        bool isCrit,
        DamageType? counterableType = null
    )
    {
        bool printsCritLine = isCrit && !(_laterStrike && _rules.MultiHitRepeatsFirstStrike);
        bool printsEffectivenessLine =
            !_laterStrike || _rules.EffectivenessLineRepeatsOnEveryStrike;
        if (Target.Battle.SubstituteHp > 0)
        {
            bool broke = SubstituteStrikes.StrikeDecoy(Target.Battle, dmg, _rules);
            _brokeSubstitute |= broke;
            SubstituteStrikes.AnnounceStrike(
                Target,
                Target.Battle.SubstituteHp,
                broke,
                _rules,
                _emitter
            );
            if (
                _rules.SoakedHitPrintsCritAndEffectiveness
                && (
                    printsCritLine
                    || _ohkoStrike
                    || (printsEffectivenessLine && effectiveness != 1.0)
                )
            )
                _emitter?.Emit(
                    new SoakedHitReport(
                        Target.Name,
                        Target.Id,
                        printsCritLine,
                        _ohkoStrike,
                        effectiveness,
                        printsEffectivenessLine,
                        _rules.HitLineWording
                    )
                );
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
                Target.Id,
                dmg,
                effectiveness,
                Target.Attributes.HP,
                Target.Attributes.MaxHP,
                isCrit,
                _hitRange?.Min,
                _hitRange?.Max,
                printsCritLine,
                _ohkoStrike,
                printsEffectivenessLine,
                _rules.HitLineWording
            )
        );
        return true;
    }

    // The shield flag consumed by the foe-directed secondaries (status / stat-drop / confusion);
    // see the _targetShieldedAtImpact field for the Gen 1 rationale.
    private bool TargetShieldedBySubstitute => _targetShieldedAtImpact;

    private bool BreakCancels(MoveEffect effect, DamageCategory category) =>
        _brokeSubstitute && _rules.SubstituteBreakCancels(effect, category);

    private void TryApplyStatus(Attack attack)
    {
        if (attack.StatusEffect == StatusCondition.None)
            return;
        if (Target.Battle.Status != StatusCondition.None)
        {
            // A status never stacks; only a pure status move announces it (GENERATION_SEAMS.md §2).
            if (attack.BaseDamage == 0)
                AnnounceAlreadyStatused(attack);
            return;
        }
        if (!Target.IsAlive())
            return;
        // A damaging move's secondary always stops at a Substitute; the hit struck the decoy.
        if (
            TargetShieldedBySubstitute
            && (attack.BaseDamage > 0 || _rules.SubstituteStopsPureStatusMove(attack))
        )
            return;

        // Only a pure status move announces this; a damaging move's hit already landed.
        bool canAffect =
            attack.BaseDamage == 0
                ? _rules.PureStatusMoveCanAffect(Target, attack.StatusEffect)
                : _rules.CanReceiveStatus(Target, attack.StatusEffect, attack.DamageType);
        if (!canAffect)
        {
            if (attack.BaseDamage == 0)
                _emitter?.Emit(StatusMoveFailure(_rules.StatusMoveNoOpAnnouncement, attack));
            return;
        }

        int chance = _rules.GetSecondaryEffectChance(attack, SecondaryEffectKind.Status);
        if (!_rules.SecondaryHits(chance, _rng))
            return;

        InflictStatus(attack);
    }

    private void InflictStatus(Attack attack)
    {
        Target.Battle.Status = attack.StatusEffect;

        if (attack.StatusEffect == StatusCondition.Sleep)
        {
            Target.Battle.SleepTurns = _rules.RollSleepTurns();
            StatusResolver.ClearRechargeOn(Target, RechargeClearingInfliction.Sleep, _rules);
        }
        else if (attack.StatusEffect == StatusCondition.Freeze)
        {
            StatusResolver.ClearRechargeOn(Target, RechargeClearingInfliction.Freeze, _rules);
        }

        _emitter?.Emit(new StatusApplied(Target.Name, Target.Id, attack.StatusEffect));
    }

    private void TryApplyStatEffect(Attack attack)
    {
        var se = attack.StatEffect;
        if (se == null)
            return;
        if (!Target.IsAlive() && se.Target == StageTarget.Foe)
            return;

        Creature affected = se.Target == StageTarget.Self ? Source : Target;

        if (
            IsFoeStatDown(attack)
            && attack.BaseDamage > 0
            && HandicapFails(attack, isSecondary: true)
        )
            return;

        // Gen 1: a Substitute shields the user from the foe's stat changes while it stands. A
        // self-targeting stat change (the user's own buff) is unaffected.
        if (se.Target == StageTarget.Foe && TargetShieldedBySubstitute)
            return;

        // Self-inflicted drops and any raise are unaffected by Mist.
        if (
            se.Target == StageTarget.Foe
            && se.Delta < 0
            && affected.Battle.HasMist
            && _rules.MistStopsStatDrop(isSecondary: attack.BaseDamage > 0)
        )
        {
            _emitter?.Emit(MistBlockLine(affected));
            return;
        }

        // IBattleRules.GetSecondaryEffectChance, not the StatEffectChance column directly (GENERATION_SEAMS.md).
        int chance = _rules.GetSecondaryEffectChance(attack, SecondaryEffectKind.StatStage);
        if (!_rules.SecondaryHits(chance, _rng))
            return;

        int before = affected.Battle.Stages.Of(se.Stat);
        int newStage = ApplyStageChange(affected, se.Stat, se.Delta);

        if (newStage != before)
        {
            _emitter?.Emit(
                new StatStageChanged(
                    affected.Name,
                    affected.Id,
                    se.Stat.ToString(),
                    se.Delta,
                    newStage
                )
            );
            return;
        }

        // The stage was already at the ±6 cap, so it didn't move. Only a PRIMARY stat move announces this
        // (IBattleRules.StatStageCapAnnouncement); a side-effect stat drop on a damaging move stays silent —
        // mirrors the redundant-confusion path in MoveEffects (dedicated-move gate + switch on the rules enum).
        if (attack.BaseDamage == 0)
        {
            BattleEvent? announcement = _rules.StatStageCapAnnouncement switch
            {
                StatCapAnnouncement.NothingHappened => new ButNothingHappened(
                    affected.Name,
                    affected.Id
                ),
                _ => null,
            };
            if (announcement is not null)
                _emitter?.Emit(announcement);
        }
    }

    private static bool IsFoeStatDown(Attack attack) =>
        attack.StatEffect is { Target: StageTarget.Foe, Delta: < 0 };

    // The AI handicap; a primary move's fail precedes accuracy, a secondary's precedes the Substitute check.
    private bool HandicapFails(Attack attack, bool isSecondary) =>
        _rules.StatDownHandicapApplies(_side, attack, isSecondary)
        && _rules.RollStatDownHandicapFails();

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
