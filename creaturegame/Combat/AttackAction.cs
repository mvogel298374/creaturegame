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

    // Whether the Target had a Substitute up at the moment this move connected — captured before the
    // damage is applied so the shield still blocks the secondary on the exact hit that breaks the decoy
    // (Gen 1: that hit struck the substitute, so its status/stat/confusion doesn't reach the user).
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
        IRandomSource? rng = null
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

        // Lock-in mechanics (two-turn, rampage, rage, bide) own their own per-turn flow — charging,
        // storing, unleashing, self-confusing — behind ILockInMechanic, rather than being branched
        // inline here. A non-lock-in move (or Struggle) resolves with lockIn == null.
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

        // Remember the move actually used so the opponent's Mirror Move can copy it. Metronome and
        // Mirror Move themselves aren't recorded here — the move they *call* records itself via its
        // inner action — so neither can ever be the foe's LastMoveUsed (that's why the Mirror Move
        // filter below only has to exclude Struggle, not them).
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

        // Mirror Move: re-execute the opponent's last used move. Like Metronome, the copied move runs in
        // full through its own inner action (which records it as this creature's last move). Fails if the
        // foe hasn't used a copyable move yet. Metronome/Mirror Move are never recorded as a LastMoveUsed
        // (see above), so only Struggle needs excluding here. Copying a lock-in move is valid Gen 1
        // behavior: Bide starts a fresh commitment, and a two-turn move (Fly) charges then auto-releases
        // through the normal lock-in path on this user.
        if (!usingStruggle && attackToUse.Effect == MoveEffect.MirrorMove)
        {
            var last = Target.Battle.LastMoveUsed;
            if (last != null && last.Name != "struggle")
                return ExecuteInner(last);
            _emitter?.Emit(new MoveMissed(Source.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        var category = usingStruggle ? DamageCategory.Standard : attackToUse.DamageCategory;

        // OHKO: fails (not just misses) per the generation's success rule — Gen 1 fails when the
        // target out-speeds the user (the level check is a Gen 2+ rule). The condition is gen-variable
        // so it lives on the seam, not inline here. Independent of the accuracy roll below.
        if (category == DamageCategory.OHKO && !_rules.OneHitKoSucceeds(Source, Target))
        {
            _emitter?.Emit(new MoveMissed(Source.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        // Accuracy check — Struggle and NeverMisses moves always hit
        if (!usingStruggle && !attackToUse.NeverMisses)
        {
            int threshold = _rules.GetHitThreshold(
                attackToUse.Accuracy,
                Source.Battle.Stages.Accuracy,
                Target.Battle.Stages.Evasion
            );
            if (_rng.Next(_rules.AccuracyRollBound) >= threshold)
            {
                _emitter?.Emit(new MoveMissed(Source.Name, attackToUse.Name ?? ""));

                // Gen 1: Self-Destruct user faints even on miss
                if (category == DamageCategory.SelfDestruct)
                    Source.Attributes.ReceiveDamage(Source.Attributes.HP);

                // Gen 1: Jump Kick / Hi Jump Kick deal crash damage to the user on a miss
                if (attackToUse.Effect == MoveEffect.Crash)
                {
                    int crash = _rules.CalculateCrashDamage(Source);
                    Source.Attributes.ReceiveDamage(crash);
                    _emitter?.Emit(new CrashDamage(Source.Name, crash, Source.Attributes.HP));
                }

                lockIn?.OnTurnEnd(lockCtx!); // a missed turn still counts toward the rampage lock
                return Task.CompletedTask;
            }
        }

        // Thaw a frozen target if the move meets the generation's thaw criteria
        bool justThawed = false;
        if (
            Target.Battle.Status == StatusCondition.Freeze
            && _rules.CanThawFrozenTarget(attackToUse)
        )
        {
            Target.Battle.Status = StatusCondition.None;
            _emitter?.Emit(new StatusCleared(Target.Name, StatusCondition.Freeze));
            justThawed = true;
        }

        // Gen 1: a target immune to the move's type takes nothing — even from moves that bypass the
        // normal damage calc (fixed / level-based / OHKO / Super Fang) and from pure-status moves
        // (Thunder Wave is Electric, so a Ground-type is immune). A damaging Standard/Drain move
        // already folds 0× into its damage (DamageDealt at 0), and Self-Destruct still detonates the
        // user — both are excluded here.
        bool isPureStatusMove =
            category == DamageCategory.Standard && !usingStruggle && attackToUse.BaseDamage == 0;
        // Gen 1: a non-damaging move almost always IGNORES the target's type immunity — Confuse Ray
        // confuses a Normal-type, Glare paralyses a Ghost, Growl lowers a Ghost's Attack, sleep / Disable
        // land regardless of the move's type matchup. Only Thunder Wave and Counter still consult the
        // chart, and which moves do is gen-variable (Gen 2 makes status moves respect immunity), so the
        // decision lives on IBattleRules, not inline. (Self-targeting moves — Recover, Swords Dance, Mist,
        // Mimic, Transform … — never consult the foe's type, so the seam never returns true for them; and
        // Leech Seed vs Grass / "Poison can't be poisoned" have their own immunity via CanBeLeechSeeded /
        // CanReceiveStatus.) Damaging moves are unaffected: they fold 0× into zero damage below.
        bool pureStatusChecksImmunity = _rules.PureStatusMoveChecksTypeImmunity(attackToUse);
        double typeImmunity = DamageCalculator.GetTypeEffectiveness(
            attackToUse.DamageType,
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
            _emitter?.Emit(new MoveHadNoEffect(Target.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        // Gen 1: Jump Kick / Hi Jump Kick crash the user on a type immunity (Fighting → Ghost = 0×),
        // not only on an accuracy miss. These are Standard-category moves, so they slip past the
        // immunity block above (which excludes Standard, since it normally folds 0× into 0 damage) and
        // would otherwise whiff harmlessly. Mirror the miss branch: announce no-effect, then crash.
        if (attackToUse.Effect == MoveEffect.Crash && typeImmunity == 0)
        {
            _emitter?.Emit(new MoveHadNoEffect(Target.Name, attackToUse.Name ?? ""));
            int crash = _rules.CalculateCrashDamage(Source);
            Source.Attributes.ReceiveDamage(crash);
            _emitter?.Emit(new CrashDamage(Source.Name, crash, Source.Attributes.HP));
            return Task.CompletedTask;
        }

        // Dream Eater only works on a sleeping target; against anything else it fails (no damage, no
        // heal). This requirement is invariant across generations — it's a property of the move, not a
        // gen-variable rule — so it's checked here rather than on the IBattleRules seam. The 50% drain
        // heal rides on the normal Drain category once the target is confirmed asleep. The failure is a
        // *state* precondition not met (target awake), like Counter having no damage to return — so it
        // reuses MoveMissed, the established path for that, not MoveHadNoEffect (which is the type-based
        // "doesn't affect" line for immunities).
        if (
            attackToUse.Effect == MoveEffect.DreamEater
            && Target.Battle.Status != StatusCondition.Sleep
        )
        {
            _emitter?.Emit(new MoveMissed(Source.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        // Snapshot the Substitute shield before any damage is applied: a secondary effect is blocked
        // if the Target was behind a decoy when the move landed, even if that same hit shatters it.
        _targetShieldedAtImpact = Target.Battle.SubstituteHp > 0;

        // Damage calculation by category
        int damage = 0;
        bool isCrit = false;

        // Reflect (vs physical) / Light Screen (vs special) double the defender's defensive stat while
        // up; the calculator ignores it on a crit. Computed once and passed into every damage call.
        int screenMult =
            (attackToUse.AttackType == AttackType.Physical && Target.Battle.HasReflect)
            || (attackToUse.AttackType == AttackType.Special && Target.Battle.HasLightScreen)
                ? _rules.ScreenDefenseMultiplier
                : 1;

        switch (category)
        {
            case DamageCategory.Standard:
            case DamageCategory.Drain:
            {
                if (usingStruggle || attackToUse.BaseDamage > 0)
                {
                    double eff = DamageCalculator.GetTypeEffectiveness(
                        attackToUse.DamageType,
                        Target.Type1,
                        Target.Type2,
                        _typeChart
                    );

                    // Multi-hit (Double Slap, Comet Punch, …): accuracy was already rolled once
                    // above; each of the 2–5 strikes rolls its own crit + variance and stops if
                    // the target faints. Normal moves take the hits == 1 path (identical output).
                    // Fixed-count movers (Double Kick = 2) carry the count as move data; variable
                    // movers (Double Slap) leave it null and draw the 2–5 count from the gen rules.
                    bool isMultiHit = !usingStruggle && attackToUse.Effect == MoveEffect.MultiHit;
                    int hits = isMultiHit
                        ? (attackToUse.MultiHitCount ?? _rules.RollMultiHitCount())
                        : 1;

                    int landed = 0;
                    bool dealtRealDamage = false;
                    for (int i = 0; i < hits && Target.IsAlive(); i++)
                    {
                        int hitDamage = DamageCalculator.CalculateDamage(
                            Source,
                            Target,
                            attackToUse,
                            _typeChart,
                            _rules,
                            out isCrit,
                            _rng,
                            screenDefenseMultiplier: screenMult
                        );
                        if (DealDamageToTarget(hitDamage, eff, isCrit, attackToUse.DamageType))
                            dealtRealDamage = true;
                        damage += hitDamage; // accumulated total gates drain/recoil/recharge below
                        landed++;
                    }

                    if (isMultiHit)
                        _emitter?.Emit(new MultiHitCompleted(landed));

                    // Rage: a raging creature that just got hit gains Attack stage(s). Triggered
                    // off the standard damage path only (the same boundary as Counter) — once per
                    // connecting attack, not per multi-hit strike, and only when the hit reached the
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
                        int heal = Math.Max(1, damage * attackToUse.DrainPercent / 100);
                        Source.Attributes.ReceiveHealing(heal);
                        _emitter?.Emit(new DrainHealed(Source.Name, heal, Source.Attributes.HP));
                    }
                }
                break;
            }

            case DamageCategory.Fixed:
                damage = attackToUse.FixedDamageValue ?? 1;
                DealDamageToTarget(damage, 1.0, false, attackToUse.DamageType);
                break;

            case DamageCategory.LevelBased:
                damage = DamageCalculator.CalculateLevelBasedDamage(Source);
                DealDamageToTarget(damage, 1.0, false, attackToUse.DamageType);
                break;

            case DamageCategory.OHKO:
                // Against a Substitute the OHKO just breaks the decoy (the soak caps at the sub's HP);
                // otherwise it removes the target's full current HP.
                damage = Target.Attributes.HP;
                DealDamageToTarget(damage, 1.0, false, attackToUse.DamageType);
                break;

            case DamageCategory.SelfDestruct:
            {
                // Gen 1: the target's Defense is halved before damage calculation, making
                // Explosion/Self-Destruct significantly stronger. The divisor is gen-variable
                // (dropped in Gen 5+), so it comes from the rules seam and is passed into the
                // calculator — we no longer mutate-and-restore the creature's real stats.
                double eff = DamageCalculator.GetTypeEffectiveness(
                    attackToUse.DamageType,
                    Target.Type1,
                    Target.Type2,
                    _typeChart
                );
                damage = DamageCalculator.CalculateDamage(
                    Source,
                    Target,
                    attackToUse,
                    _typeChart,
                    _rules,
                    out isCrit,
                    _rng,
                    defenseDivisor: _rules.SelfDestructDefenseDivisor,
                    screenDefenseMultiplier: screenMult
                );

                DealDamageToTarget(damage, eff, isCrit, attackToUse.DamageType);

                // User faints unconditionally (already handled miss → faint above)
                Source.Attributes.ReceiveDamage(Source.Attributes.HP);
                break;
            }

            case DamageCategory.SuperFang:
                damage = DamageCalculator.CalculateSuperFangDamage(Target);
                DealDamageToTarget(damage, 1.0, false, attackToUse.DamageType);
                break;

            case DamageCategory.Psywave:
                // Gen 1: a random 1..floor(1.5×level), ignoring Attack/Defense, type effectiveness,
                // STAB and crits. The magnitude is gen-variable, so it comes from the rules seam.
                damage = _rules.RollPsywaveDamage(Source, _rng);
                DealDamageToTarget(damage, 1.0, false, attackToUse.DamageType);
                break;
        }

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

    // Runs another move in full as this turn's action — used by Metronome and Mirror Move, which call a
    // move chosen at runtime. The move runs through a fresh AttackAction on a temporary wrapper (so the
    // creature's own PP/moveset is untouched) and records itself as the user's last move.
    private Task ExecuteInner(Attack move) =>
        new AttackAction(
            Source,
            Target,
            new PokemonAttack(move),
            _typeChart,
            _rules,
            _emitter,
            _movePool,
            _rng
        ).ExecuteAsync();

    // Bide stores every hit the committed user takes — any damage category — to unleash 2× on release.
    private void AccumulateBideDamage(int dmg)
    {
        if (Target.Battle.BideTurnsRemaining > 0)
            Target.Battle.BideDamageAccumulated += dmg;
    }

    // Applies <paramref name="dmg"/> to the Target, honoring an active Substitute. Gen 1: while a
    // Substitute stands, the decoy soaks the whole hit — the user's HP is untouched and any overflow is
    // lost (it does NOT carry through to the user, even from an OHKO). Returns true when the real
    // creature took the damage (so a caller can run on-real-hit follow-ups like Rage), false when the
    // Substitute absorbed it. Every damage category routes through here so the soak can't be missed by
    // one branch (the recurring "hook added to only the Standard path" defect).
    //
    // <paramref name="counterableType"/> is the move's type when the hit should be recordable for the
    // target's Counter (the value Counter reads back as 2× the last Normal/Fighting damage taken). It's
    // recorded centrally here — gated on real damage landing — so every damaging category (Standard,
    // Fixed, level-based, OHKO, Self-Destruct, Super Fang, Psywave) is counterable through one path, not
    // just the Standard loop. Callers that must stay non-counterable (Bide's unleash) pass null.
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

    // True while a foe-directed secondary (status, stat-drop, confusion) should be blocked because the
    // Target is hiding behind a Substitute — Gen 1: the decoy shields the user from the opponent's
    // status and stat changes while it stands.
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
        if (_rng.Next(1, 101) > chance)
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
        if (_rng.Next(1, 101) > chance)
            return;

        int newStage = ApplyStageChange(affected, se.Stat, se.Delta);
        _emitter?.Emit(new StatStageChanged(affected.Name, se.Stat.ToString(), se.Delta, newStage));
    }

    // Post-damage move effects (Haze, Counter, Reflect, Transform, Rest, Substitute …) each live behind
    // IMoveEffect in the MoveEffects registry, keyed by MoveEffect — mirroring the ILockInMechanic /
    // LockInMechanics.For pattern. Exactly one effect runs per move; a move with no post-damage effect
    // resolves to null. The effect reaches the shared damage helper through the context's DealDamage
    // delegate so the Substitute soak / Bide accumulation / Counter recording stay centralized here.
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
