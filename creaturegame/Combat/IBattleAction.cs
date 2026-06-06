using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public interface IBattleAction
{
    Creature Source { get; }
    int Priority { get; }
    Task ExecuteAsync();
}

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
        if (Source.IsRecharging)
        {
            Source.IsRecharging = false;
            _emitter?.Emit(new Recharging(Source.Name));
            return Task.CompletedTask;
        }

        bool usingStruggle = _selectedMove == null;
        Attack attackToUse = usingStruggle ? Source.Struggle : _selectedMove!.Base;

        // Two-turn move: release turn is when IsTwoTurnCharging was set by the charge turn
        bool isTwoTurn = !usingStruggle && attackToUse.Effect == MoveEffect.TwoTurn;
        bool isReleaseTurn = isTwoTurn && Source.IsTwoTurnCharging;

        // Rampage (Thrash/Petal Dance): a continuation turn is one already locked in from before.
        bool isRampage = !usingStruggle && attackToUse.Effect == MoveEffect.Rampage;
        bool rampageContinuation = isRampage && Source.RampageTurnsRemaining > 0;

        // Rage: once used, the user is locked into Rage indefinitely (auto-repeated by Battle). A
        // continuation turn is any after the first, identified by the lock already being set.
        bool isRage = !usingStruggle && attackToUse.Effect == MoveEffect.Rage;
        bool rageContinuation = isRage && Source.IsRaging;

        // Bide: commits the user for 2–3 turns (storing damage), then unleashes 2×. A continuation
        // turn is one already committed from before (the lock auto-repeats via Battle).
        bool isBide = !usingStruggle && attackToUse.Effect == MoveEffect.Bide;
        bool bideContinuation = isBide && Source.BideTurnsRemaining > 0;

        // A turn that continues an already-charged/committed lock-in: a two-turn release, or a rampage /
        // rage / bide continuation. PP was spent on the first turn of the lock, so these don't re-spend.
        bool isLockedInContinuation =
            isReleaseTurn || rampageContinuation || rageContinuation || bideContinuation;

        // PP decremented on the first turn only (don't double-spend a lock-in continuation).
        if (!usingStruggle && !isLockedInContinuation)
            _selectedMove!.PowerPointsCurrent--;

        // Charge phase: wind up and defer damage to the next turn
        if (isTwoTurn && !isReleaseTurn)
        {
            Source.IsTwoTurnCharging = true;
            Source.ChargingMove = _selectedMove;
            _emitter?.Emit(new ChargingUp(Source.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        // Clear two-turn state on the release turn
        if (isReleaseTurn)
        {
            Source.IsTwoTurnCharging = false;
            Source.ChargingMove = null;
        }

        // Bide: on first use commit for 2–3 turns and reset the absorbed total; every committed turn
        // just stores (no attack) until the counter hits 0, when it falls through to unleash below.
        if (isBide)
        {
            if (!bideContinuation)
            {
                Source.BideTurnsRemaining = _rules.RollBideTurns();
                Source.BideDamageAccumulated = 0;
                Source.BideMove = _selectedMove;
            }
            Source.BideTurnsRemaining--;
            if (Source.BideTurnsRemaining > 0)
            {
                _emitter?.Emit(new BideStoring(Source.Name));
                return Task.CompletedTask;
            }
        }

        _emitter?.Emit(new MoveUsed(Source.Name, attackToUse.Name ?? ""));

        // Remember the move actually used so the opponent's Mirror Move can copy it. Metronome and
        // Mirror Move themselves aren't recorded here — the move they *call* records itself via its
        // inner action — so neither can ever be the foe's LastMoveUsed (that's why the Mirror Move
        // filter below only has to exclude Struggle, not them).
        if (
            !usingStruggle
            && attackToUse.Effect is not (MoveEffect.Metronome or MoveEffect.MirrorMove)
        )
            Source.LastMoveUsed = attackToUse;

        // Bide release: the committed turns are done — unleash double the damage absorbed. The damage
        // is typeless and never misses; like the other non-standard damage categories (Fixed, OHKO, …)
        // it is deliberately not recorded as counterable, so Counter can't reflect it (matching this
        // engine's "Counter only answers standard-path damage" simplification). Fails if nothing was
        // stored. BideMove clears so Battle stops auto-repeating.
        if (isBide)
        {
            int unleashed = Source.BideDamageAccumulated * _rules.BideDamageMultiplier;
            Source.BideMove = null;
            if (unleashed > 0 && Target.IsAlive())
            {
                // Routes through the shared helper so a foe Substitute soaks the unleash too.
                DealDamageToTarget(unleashed, 1.0, false);
            }
            else
            {
                _emitter?.Emit(new MoveMissed(Source.Name, attackToUse.Name ?? ""));
            }
            return Task.CompletedTask;
        }

        // Rampage: on the first turn roll the lock duration and remember the move; every turn counts
        // down (even a miss), and when it expires the user confuses itself. EndRampageIfDone() runs
        // after the attack resolves — including the miss early-return below — so the lock can't hang.
        if (isRampage && !rampageContinuation)
        {
            Source.RampageTurnsRemaining = _rules.RollRampageTurns();
            Source.RampageMove = _selectedMove;
        }
        if (isRampage)
            Source.RampageTurnsRemaining--;

        // Rage: lock the user in on first use (even a miss locks). Battle force-selects RageMove
        // every turn thereafter; the lock clears only on the per-battle reset. The Attack-on-hit
        // raise happens in the standard damage path below, when this creature is the one hit.
        if (isRage && !rageContinuation)
        {
            Source.IsRaging = true;
            Source.RageMove = _selectedMove;
        }

        void EndRampageIfDone()
        {
            if (!isRampage || Source.RampageTurnsRemaining > 0)
                return;
            Source.RampageMove = null;
            if (Source.IsAlive() && Source.ConfusedTurns == 0)
            {
                Source.ConfusedTurns = _rules.RollConfusionTurns();
                _emitter?.Emit(new ConfusionStarted(Source.Name));
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
            var last = Target.LastMoveUsed;
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
                Source.Stages.Accuracy,
                Target.Stages.Evasion
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

                EndRampageIfDone(); // a missed turn still counts toward the rampage lock
                return Task.CompletedTask;
            }
        }

        // Thaw a frozen target if the move meets the generation's thaw criteria
        bool justThawed = false;
        if (Target.Status == StatusCondition.Freeze && _rules.CanThawFrozenTarget(attackToUse))
        {
            Target.Status = StatusCondition.None;
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
        // A pure-status move is only blocked by the target's type immunity when it actually acts on
        // the foe (status, confusion, Leech Seed, Disable, Counter's reflected damage, a foe stat
        // drop). Self-targeting moves (Recover, Swords Dance, Mist, Haze, …) never consult the
        // target's type, so a Normal-type self-buff still works against a Ghost. Mimic is deliberately
        // excluded too: it copies a move rather than acting on the foe, so it isn't type-blocked.
        bool targetsFoe =
            attackToUse.StatusEffect != StatusCondition.None
            || attackToUse.Effect
                is MoveEffect.Confuse
                    or MoveEffect.LeechSeed
                    or MoveEffect.Disable
                    or MoveEffect.Counter
            || attackToUse.StatEffect is { Target: StageTarget.Foe };
        double typeImmunity = DamageCalculator.GetTypeEffectiveness(
            attackToUse.DamageType,
            Target.Type1,
            Target.Type2,
            _typeChart
        );
        if (
            typeImmunity == 0
            && (
                (isPureStatusMove && targetsFoe)
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

        // Dream Eater only works on a sleeping target; against anything else it fails (no damage, no
        // heal). This requirement is invariant across generations — it's a property of the move, not a
        // gen-variable rule — so it's checked here rather than on the IBattleRules seam. The 50% drain
        // heal rides on the normal Drain category once the target is confirmed asleep. The failure is a
        // *state* precondition not met (target awake), like Counter having no damage to return — so it
        // reuses MoveMissed, the established path for that, not MoveHadNoEffect (which is the type-based
        // "doesn't affect" line for immunities).
        if (attackToUse.Effect == MoveEffect.DreamEater && Target.Status != StatusCondition.Sleep)
        {
            _emitter?.Emit(new MoveMissed(Source.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        // Snapshot the Substitute shield before any damage is applied: a secondary effect is blocked
        // if the Target was behind a decoy when the move landed, even if that same hit shatters it.
        _targetShieldedAtImpact = Target.SubstituteHp > 0;

        // Damage calculation by category
        int damage = 0;
        bool isCrit = false;

        // Reflect (vs physical) / Light Screen (vs special) double the defender's defensive stat while
        // up; the calculator ignores it on a crit. Computed once and passed into every damage call.
        int screenMult =
            (attackToUse.AttackType == AttackType.Physical && Target.HasReflect)
            || (attackToUse.AttackType == AttackType.Special && Target.HasLightScreen)
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
                        if (DealDamageToTarget(hitDamage, eff, isCrit))
                        {
                            // Counter answers the last hit that actually connected with the creature
                            // (a hit soaked by a Substitute didn't, so it doesn't update this).
                            Target.LastDamageTaken = hitDamage;
                            Target.LastDamageType = attackToUse.DamageType;
                            dealtRealDamage = true;
                        }
                        damage += hitDamage; // accumulated total gates drain/recoil/recharge below
                        landed++;
                    }

                    if (isMultiHit)
                        _emitter?.Emit(new MultiHitCompleted(landed));

                    // Rage: a raging creature that just got hit gains Attack stage(s). Triggered
                    // off the standard damage path only (the same boundary as Counter) — once per
                    // connecting attack, not per multi-hit strike, and only when the hit reached the
                    // creature itself (a Substitute soak doesn't enrage it). Reuses StatStageChanged.
                    if (dealtRealDamage && Target.IsRaging && Target.IsAlive())
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
                DealDamageToTarget(damage, 1.0, false);
                break;

            case DamageCategory.LevelBased:
                damage = DamageCalculator.CalculateLevelBasedDamage(Source);
                DealDamageToTarget(damage, 1.0, false);
                break;

            case DamageCategory.OHKO:
                // Against a Substitute the OHKO just breaks the decoy (the soak caps at the sub's HP);
                // otherwise it removes the target's full current HP.
                damage = Target.Attributes.HP;
                DealDamageToTarget(damage, 1.0, false);
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

                DealDamageToTarget(damage, eff, isCrit);

                // User faints unconditionally (already handled miss → faint above)
                Source.Attributes.ReceiveDamage(Source.Attributes.HP);
                break;
            }

            case DamageCategory.SuperFang:
                damage = DamageCalculator.CalculateSuperFangDamage(Target);
                DealDamageToTarget(damage, 1.0, false);
                break;

            case DamageCategory.Psywave:
                // Gen 1: a random 1..floor(1.5×level), ignoring Attack/Defense, type effectiveness,
                // STAB and crits. The magnitude is gen-variable, so it comes from the rules seam.
                damage = _rules.RollPsywaveDamage(Source, _rng);
                DealDamageToTarget(damage, 1.0, false);
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
            Source.IsRecharging = true;

        if (!justThawed)
            TryApplyStatus(attackToUse);

        TryApplyStatEffect(attackToUse);
        TryApplyMoveEffect(attackToUse, damage);

        EndRampageIfDone(); // confuse the user if this was the rampage's last turn

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
        if (Target.BideTurnsRemaining > 0)
            Target.BideDamageAccumulated += dmg;
    }

    // Applies <paramref name="dmg"/> to the Target, honoring an active Substitute. Gen 1: while a
    // Substitute stands, the decoy soaks the whole hit — the user's HP is untouched and any overflow is
    // lost (it does NOT carry through to the user, even from an OHKO). Returns true when the real
    // creature took the damage (so a caller can run on-real-hit follow-ups like Counter recording or
    // Rage), false when the Substitute absorbed it. Every damage category routes through here so the
    // soak can't be missed by one branch (the recurring "hook added to only the Standard path" defect).
    private bool DealDamageToTarget(int dmg, double effectiveness, bool isCrit)
    {
        if (Target.SubstituteHp > 0)
        {
            Target.SubstituteHp = Math.Max(0, Target.SubstituteHp - dmg);
            if (Target.SubstituteHp == 0)
                _emitter?.Emit(new SubstituteFaded(Target.Name));
            else
                _emitter?.Emit(new SubstituteAbsorbedHit(Target.Name, Target.SubstituteHp));
            return false;
        }

        Target.Attributes.ReceiveDamage(dmg);
        AccumulateBideDamage(dmg);
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
        if (Target.Status != StatusCondition.None)
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

        Target.Status = attack.StatusEffect;

        if (attack.StatusEffect == StatusCondition.Sleep)
            Target.SleepTurns = _rules.RollSleepTurns();

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
        if (se.Target == StageTarget.Foe && se.Delta < 0 && affected.HasMist)
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

    private void TryApplyMoveEffect(Attack attack, int damage)
    {
        switch (attack.Effect)
        {
            case MoveEffect.Haze:
                Source.ResetBattleState();
                Target.ResetBattleState();
                _emitter?.Emit(new HazeClearedStages());
                break;

            case MoveEffect.Flinch:
                if (Target.IsAlive())
                {
                    int chance = _rules.GetSecondaryEffectChance(
                        attack,
                        SecondaryEffectKind.Flinch
                    );
                    if (_rng.Next(1, 101) <= chance)
                        Target.IsFlinched = true;
                }
                break;

            case MoveEffect.LeechSeed:
                if (Target.IsAlive() && !_rules.CanBeLeechSeeded(Target))
                    _emitter?.Emit(new MoveHadNoEffect(Target.Name, attack.Name ?? "")); // Grass-types are immune
                else if (!Target.HasLeechSeed && Target.IsAlive())
                {
                    Target.HasLeechSeed = true;
                    _emitter?.Emit(new LeechSeedApplied(Target.Name));
                }
                break;

            case MoveEffect.Binding:
                if (Target.BindingTurnsRemaining == 0 && damage > 0 && Target.IsAlive())
                {
                    Target.BindingTurnsRemaining = _rules.RollBindingTurns();
                    _emitter?.Emit(new BindingStarted(Target.Name, attack.Name ?? ""));
                }
                break;

            case MoveEffect.PayDay:
                // Deals normal damage (handled above) and scatters coins on hit; the money is
                // collected after the battle. No economy yet — the mechanic is the event.
                _emitter?.Emit(
                    new CoinsScattered(Source.Name, _rules.PayDayCoinMultiplier * Source.Level)
                );
                break;

            case MoveEffect.Recoil:
                // Take Down / Double-Edge: the user takes back a fraction of the damage it dealt.
                if (damage > 0 && Source.IsAlive())
                {
                    int recoil = _rules.CalculateRecoilDamage(damage);
                    Source.Attributes.ReceiveDamage(recoil);
                    _emitter?.Emit(new RecoilDamage(Source.Name, recoil, Source.Attributes.HP));
                }
                break;

            case MoveEffect.Disable:
                // Disable locks one of the target's moves out of selection for several turns; it
                // fails if the target already has a move disabled or has no usable move. The
                // duration is gen-variable and lives on the seam (IBattleRules.RollDisableTurns).
                // The *which move* picked is also gen-variable — Gen 1 & 2 choose a random PP-bearing
                // move (below), Gen 3+ target the last-used move. We keep that choice inline and
                // documented rather than on a single-use seam member we wouldn't vary until Gen 3.
                // The lock is *enforced* at move-selection time (Battle/IBattleInput skip
                // DisabledMove); the counter ticks down end-of-turn (StatusResolver) and re-enables.
                if (Target.IsAlive() && Target.DisableTurnsRemaining == 0)
                {
                    var disableable = Target.MoveSet.Where(m => m.PowerPointsCurrent > 0).ToList();
                    if (disableable.Count > 0)
                    {
                        var locked = disableable[_rng.Next(disableable.Count)];
                        Target.DisabledMove = locked;
                        Target.DisableTurnsRemaining = _rules.RollDisableTurns();
                        _emitter?.Emit(new MoveDisabled(Target.Name, locked.Base.Name ?? ""));
                    }
                }
                break;

            case MoveEffect.Counter:
                // Gen 1: returns double the damage the user last took from a Normal/Fighting move.
                // The −5 priority (move data) resolves Counter after the opponent's hit, so it
                // counters this turn's damage. Fails if no qualifying damage was taken — Gen 1 keeps
                // the last value until overwritten, so this can fire off a previous turn (a quirk we
                // preserve). Fixed/level-based/self damage isn't recorded, so it isn't counterable.
                // Type immunity (Counter is Fighting ⇒ Ghost is immune) is handled by the pure-status
                // immunity guard above — Counter has BaseDamage 0, so an immune target already
                // short-circuited to MoveHadNoEffect and never reaches here.
                if (
                    Target.IsAlive()
                    && Source.LastDamageTaken > 0
                    && Source.LastDamageType is DamageType.Normal or DamageType.Fighting
                )
                {
                    int countered = Source.LastDamageTaken * 2;
                    if (DealDamageToTarget(countered, 1.0, false))
                    {
                        Target.LastDamageTaken = countered;
                        Target.LastDamageType = attack.DamageType;
                    }
                }
                else
                {
                    _emitter?.Emit(new MoveMissed(Source.Name, attack.Name ?? ""));
                }
                break;

            case MoveEffect.Mist:
                // Mist shrouds the user; the opponent can't lower its stats until the battle ends
                // (enforced in TryApplyStatEffect). Self-targeting, no damage.
                if (!Source.HasMist)
                {
                    Source.HasMist = true;
                    _emitter?.Emit(new MistApplied(Source.Name));
                }
                break;

            case MoveEffect.Reflect:
                // Doubles the user's Defense vs physical damage until the battle ends (self-targeting).
                if (!Source.HasReflect)
                {
                    Source.HasReflect = true;
                    _emitter?.Emit(new ScreenApplied(Source.Name, "Reflect"));
                }
                break;

            case MoveEffect.LightScreen:
                // Doubles the user's Special vs special damage until the battle ends (self-targeting).
                if (!Source.HasLightScreen)
                {
                    Source.HasLightScreen = true;
                    _emitter?.Emit(new ScreenApplied(Source.Name, "Light Screen"));
                }
                break;

            case MoveEffect.FocusEnergy:
                // Self-targeting crit modifier — Gen 1's bugged ÷4 is applied in Gen1BattleRules.GetCritChance.
                if (!Source.HasFocusEnergy)
                {
                    Source.HasFocusEnergy = true;
                    _emitter?.Emit(new FocusEnergyApplied(Source.Name));
                }
                break;

            case MoveEffect.Heal:
                // Recover / Soft-Boiled: restore a fraction of max HP to the user (capped at max by
                // ReceiveHealing). The heal fraction is gen-variable, so it comes from the rules seam.
                if (Source.IsAlive())
                {
                    int hpBefore = Source.Attributes.HP;
                    int heal = Math.Max(
                        1,
                        (int)(Source.Attributes.MaxHP * _rules.RecoverHealFraction)
                    );
                    Source.Attributes.ReceiveHealing(heal);
                    // Report the amount actually restored (ReceiveHealing caps at max), not the request.
                    _emitter?.Emit(
                        new Healed(
                            Source.Name,
                            Source.Attributes.HP - hpBefore,
                            Source.Attributes.HP
                        )
                    );
                }
                break;

            case MoveEffect.Mimic:
                // Gen 1: copy a random move from the target's set; it replaces Mimic for the rest of
                // the battle (Battle restores it on battle end). The user only copies this turn — no
                // damage — then can use the copied move on later turns. Fails if already mimicked or
                // the target has no copyable move.
                if (Target.IsAlive() && _selectedMove != null && Source.MimicWrapper == null)
                {
                    var copyable = Target
                        .MoveSet.Where(m =>
                            m.Base.Effect != MoveEffect.Mimic && m.Base.Name != "struggle"
                        )
                        .ToList();
                    if (copyable.Count > 0)
                    {
                        var chosen = copyable[_rng.Next(copyable.Count)].Base;
                        Source.MimicWrapper = _selectedMove;
                        Source.MimicOriginalBase = _selectedMove.Base;
                        _selectedMove.Base = chosen;
                        _emitter?.Emit(new MimicLearned(Source.Name, chosen.Name ?? ""));
                    }
                }
                break;

            case MoveEffect.Rest:
                // Gen 1: the user fully restores HP, cures any major status, then forces itself asleep
                // for a fixed number of turns (the duration is gen-variable ⇒ IBattleRules.RestSleepTurns,
                // distinct from the random RollSleepTurns). Fails if already at full HP — a state
                // precondition not met, so it reuses MoveMissed like Counter/Dream Eater, not the
                // type-based MoveHadNoEffect. Self-targeting, so the foe-immunity guard never blocks it.
                if (Source.Attributes.HP >= Source.Attributes.MaxHP)
                {
                    _emitter?.Emit(new MoveMissed(Source.Name, attack.Name ?? ""));
                    break;
                }
                int restored = Source.Attributes.MaxHP - Source.Attributes.HP;
                Source.Attributes.ReceiveHealing(restored); // heals exactly to full
                // Sleep overwrites any prior major status (Gen 1 allows only one) — Rest's documented cure.
                Source.Status = StatusCondition.Sleep;
                Source.SleepTurns = _rules.RestSleepTurns;
                _emitter?.Emit(new Healed(Source.Name, restored, Source.Attributes.HP));
                _emitter?.Emit(new StatusApplied(Source.Name, StatusCondition.Sleep));
                break;

            case MoveEffect.Substitute:
                // Gen 1: spend floor(maxHP/4) HP to raise a decoy with floor(maxHP/4)+1 HP that soaks
                // the foe's hits (absorption handled in DealDamageToTarget; status/stat shielding in the
                // Try* methods). Fails if a Substitute is already up or the user can't pay (current HP ≤
                // the cost) — a state precondition, so it reuses MoveMissed like Counter/Rest. The ¼ cost
                // is gen-invariant (¼ in every generation; litmus "would Gen 2 change it?" → no), so it's
                // inline rather than on the seam. Self-targeting ⇒ the foe-immunity guard never blocks it.
                if (Source.SubstituteHp > 0)
                {
                    _emitter?.Emit(new MoveMissed(Source.Name, attack.Name ?? ""));
                    break;
                }
                int cost = Source.Attributes.MaxHP / 4;
                if (Source.Attributes.HP <= cost)
                {
                    _emitter?.Emit(new MoveMissed(Source.Name, attack.Name ?? ""));
                    break;
                }
                Source.Attributes.ReceiveDamage(cost);
                Source.SubstituteHp = cost + 1;
                _emitter?.Emit(new SubstitutePutUp(Source.Name, Source.SubstituteHp));
                break;

            case MoveEffect.Splash:
                // Splash does nothing by design — Gen 1's "But nothing happened!". It's a no-op
                // move (no damage, no status, no stat change), so the only observable behavior is
                // the message. Invariant across generations, so it stays inline rather than on the
                // rules seam.
                _emitter?.Emit(new ButNothingHappened(Source.Name));
                break;

            case MoveEffect.Confuse:
                // Confusion is independent of major status (a creature can be both). Gen 1
                // confusion doesn't stack — only applies if the target isn't already confused.
                // EffectChance gates secondary confusion on damaging moves (Psybeam 10%);
                // pure confusion moves (Supersonic, Confuse Ray) have no chance ⇒ always land.
                // A Substitute shields the user from the foe's confusion while it stands (Gen 1).
                if (Target.IsAlive() && Target.ConfusedTurns == 0 && !TargetShieldedBySubstitute)
                {
                    int chance = _rules.GetSecondaryEffectChance(
                        attack,
                        SecondaryEffectKind.Confuse
                    );
                    if (_rng.Next(1, 101) <= chance)
                    {
                        Target.ConfusedTurns = _rules.RollConfusionTurns();
                        _emitter?.Emit(new ConfusionStarted(Target.Name));
                    }
                }
                break;
        }
    }

    private static int ApplyStageChange(Creature creature, StageStat stat, int delta) =>
        stat switch
        {
            StageStat.Attack => After(
                () => creature.Stages.RaiseAttack(delta),
                () => creature.Stages.Attack
            ),
            StageStat.Defense => After(
                () => creature.Stages.RaiseDefense(delta),
                () => creature.Stages.Defense
            ),
            StageStat.Special => After(
                () => creature.Stages.RaiseSpecial(delta),
                () => creature.Stages.Special
            ),
            StageStat.Speed => After(
                () => creature.Stages.RaiseSpeed(delta),
                () => creature.Stages.Speed
            ),
            StageStat.Accuracy => After(
                () => creature.Stages.RaiseAccuracy(delta),
                () => creature.Stages.Accuracy
            ),
            StageStat.Evasion => After(
                () => creature.Stages.RaiseEvasion(delta),
                () => creature.Stages.Evasion
            ),
            _ => 0,
        };

    private static int After(Action mutate, Func<int> read)
    {
        mutate();
        return read();
    }
}
