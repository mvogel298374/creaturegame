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
    private readonly ITypeChart              _typeChart;
    private readonly IBattleRules            _rules;
    private readonly IBattleEventEmitter?    _emitter;
    private readonly IReadOnlyList<Attack>   _movePool;
    private readonly IRandomSource           _rng;

    // Null means Struggle — Battle passes null when the source has no selectable move
    // (out of PP, or its only move is Disabled), bypassing IBattleInput.
    private readonly PokemonAttack? _selectedMove;

    public AttackAction(Creature source, Creature target,
                        PokemonAttack? selectedMove, ITypeChart typeChart,
                        IBattleRules? rules = null, IBattleEventEmitter? emitter = null,
                        IReadOnlyList<Attack>? movePool = null, IRandomSource? rng = null)
    {
        Source        = source;
        Target        = target;
        _typeChart    = typeChart;
        _rules        = rules ?? Gen1BattleRules.Instance;
        _emitter      = emitter;
        _selectedMove = selectedMove;
        _movePool     = movePool ?? Array.Empty<Attack>();
        _rng          = rng ?? SystemRandomSource.Instance;
        Priority      = selectedMove?.Base.Priority ?? 0;
    }

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive()) return Task.CompletedTask;

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
        bool isTwoTurn     = !usingStruggle && attackToUse.Effect == MoveEffect.TwoTurn;
        bool isReleaseTurn = isTwoTurn && Source.IsTwoTurnCharging;

        // Rampage (Thrash/Petal Dance): a continuation turn is one already locked in from before.
        bool isRampage           = !usingStruggle && attackToUse.Effect == MoveEffect.Rampage;
        bool rampageContinuation = isRampage && Source.RampageTurnsRemaining > 0;

        // Rage: once used, the user is locked into Rage indefinitely (auto-repeated by Battle). A
        // continuation turn is any after the first, identified by the lock already being set.
        bool isRage           = !usingStruggle && attackToUse.Effect == MoveEffect.Rage;
        bool rageContinuation = isRage && Source.IsRaging;

        // PP decremented on the first turn only (two-turn release, rampage continuations, and rage
        // continuations were already charged for; don't double-spend).
        if (!usingStruggle && !isReleaseTurn && !rampageContinuation && !rageContinuation)
            _selectedMove!.PowerPointsCurrent--;

        // Charge phase: wind up and defer damage to the next turn
        if (isTwoTurn && !isReleaseTurn)
        {
            Source.IsTwoTurnCharging = true;
            Source.ChargingMove      = _selectedMove;
            _emitter?.Emit(new ChargingUp(Source.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        // Clear two-turn state on the release turn
        if (isReleaseTurn)
        {
            Source.IsTwoTurnCharging = false;
            Source.ChargingMove      = null;
        }

        _emitter?.Emit(new MoveUsed(Source.Name, attackToUse.Name ?? ""));

        // Rampage: on the first turn roll the lock duration and remember the move; every turn counts
        // down (even a miss), and when it expires the user confuses itself. EndRampageIfDone() runs
        // after the attack resolves — including the miss early-return below — so the lock can't hang.
        if (isRampage && !rampageContinuation)
        {
            Source.RampageTurnsRemaining = _rules.RollRampageTurns();
            Source.RampageMove           = _selectedMove;
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
            if (!isRampage || Source.RampageTurnsRemaining > 0) return;
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
                .Where(m => m.Effect != MoveEffect.Metronome
                         && m.Name != "mirror-move"
                         && m.Name != "struggle")
                .ToList();

            if (eligible.Count > 0)
            {
                var chosen = eligible[_rng.Next(eligible.Count)];
                var inner  = new AttackAction(Source, Target,
                    new PokemonAttack(chosen), _typeChart, _rules, _emitter, _movePool, _rng);
                return inner.ExecuteAsync();
            }
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
                attackToUse.Accuracy, Source.Stages.Accuracy, Target.Stages.Evasion);
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

                EndRampageIfDone();   // a missed turn still counts toward the rampage lock
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
        bool isPureStatusMove = category == DamageCategory.Standard && !usingStruggle
                                && attackToUse.BaseDamage == 0;
        // A pure-status move is only blocked by the target's type immunity when it actually acts on
        // the foe (status, confusion, Leech Seed, Disable, Counter's reflected damage, a foe stat
        // drop). Self-targeting moves (Recover, Swords Dance, Mist, Haze, …) never consult the
        // target's type, so a Normal-type self-buff still works against a Ghost. Mimic is deliberately
        // excluded too: it copies a move rather than acting on the foe, so it isn't type-blocked.
        bool targetsFoe = attackToUse.StatusEffect != StatusCondition.None
                          || attackToUse.Effect is MoveEffect.Confuse or MoveEffect.LeechSeed
                                                or MoveEffect.Disable or MoveEffect.Counter
                          || attackToUse.StatEffect is { Target: StageTarget.Foe };
        double typeImmunity = DamageCalculator.GetTypeEffectiveness(
            attackToUse.DamageType, Target.Type1, Target.Type2, _typeChart);
        if (typeImmunity == 0 && ((isPureStatusMove && targetsFoe) || category is DamageCategory.Fixed
                or DamageCategory.LevelBased or DamageCategory.OHKO or DamageCategory.SuperFang))
        {
            _emitter?.Emit(new MoveHadNoEffect(Target.Name, attackToUse.Name ?? ""));
            return Task.CompletedTask;
        }

        // Damage calculation by category
        int  damage = 0;
        bool isCrit = false;

        switch (category)
        {
            case DamageCategory.Standard:
            case DamageCategory.Drain:
            {
                if (usingStruggle || attackToUse.BaseDamage > 0)
                {
                    double eff = DamageCalculator.GetTypeEffectiveness(
                        attackToUse.DamageType, Target.Type1, Target.Type2, _typeChart);

                    // Multi-hit (Double Slap, Comet Punch, …): accuracy was already rolled once
                    // above; each of the 2–5 strikes rolls its own crit + variance and stops if
                    // the target faints. Normal moves take the hits == 1 path (identical output).
                    // Fixed-count movers (Double Kick = 2) carry the count as move data; variable
                    // movers (Double Slap) leave it null and draw the 2–5 count from the gen rules.
                    bool isMultiHit = !usingStruggle && attackToUse.Effect == MoveEffect.MultiHit;
                    int  hits       = isMultiHit
                        ? (attackToUse.MultiHitCount ?? _rules.RollMultiHitCount())
                        : 1;

                    int landed = 0;
                    for (int i = 0; i < hits && Target.IsAlive(); i++)
                    {
                        int hitDamage = DamageCalculator.CalculateDamage(
                            Source, Target, attackToUse, _typeChart, _rules, out isCrit, _rng);
                        Target.Attributes.ReceiveDamage(hitDamage);
                        Target.LastDamageTaken = hitDamage;          // for Counter (2× the last hit)
                        Target.LastDamageType  = attackToUse.DamageType;
                        damage += hitDamage;   // accumulated total gates drain/recoil/recharge below
                        landed++;
                        _emitter?.Emit(new DamageDealt(Target.Name, hitDamage, eff,
                            Target.Attributes.HP, Target.Attributes.MaxHP, isCrit));
                    }

                    if (isMultiHit)
                        _emitter?.Emit(new MultiHitCompleted(landed));

                    // Rage: a raging creature that just got hit gains Attack stage(s). Triggered
                    // off the standard damage path only (the same boundary as Counter) — once per
                    // connecting attack, not per multi-hit strike. Reuses StatStageChanged.
                    if (landed > 0 && Target.IsRaging && Target.IsAlive())
                    {
                        int newStage = ApplyStageChange(Target, StageStat.Attack, _rules.RageAttackStagesPerHit);
                        _emitter?.Emit(new StatStageChanged(
                            Target.Name, StageStat.Attack.ToString(), _rules.RageAttackStagesPerHit, newStage));
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
                Target.Attributes.ReceiveDamage(damage);
                _emitter?.Emit(new DamageDealt(Target.Name, damage, 1.0,
                    Target.Attributes.HP, Target.Attributes.MaxHP));
                break;

            case DamageCategory.LevelBased:
                damage = DamageCalculator.CalculateLevelBasedDamage(Source);
                Target.Attributes.ReceiveDamage(damage);
                _emitter?.Emit(new DamageDealt(Target.Name, damage, 1.0,
                    Target.Attributes.HP, Target.Attributes.MaxHP));
                break;

            case DamageCategory.OHKO:
                damage = Target.Attributes.HP;
                Target.Attributes.ReceiveDamage(damage);
                _emitter?.Emit(new DamageDealt(Target.Name, damage, 1.0,
                    Target.Attributes.HP, Target.Attributes.MaxHP));
                break;

            case DamageCategory.SelfDestruct:
            {
                // Gen 1: the target's Defense is halved before damage calculation, making
                // Explosion/Self-Destruct significantly stronger. The divisor is gen-variable
                // (dropped in Gen 5+), so it comes from the rules seam and is passed into the
                // calculator — we no longer mutate-and-restore the creature's real stats.
                double eff = DamageCalculator.GetTypeEffectiveness(
                    attackToUse.DamageType, Target.Type1, Target.Type2, _typeChart);
                damage = DamageCalculator.CalculateDamage(
                    Source, Target, attackToUse, _typeChart, _rules, out isCrit, _rng,
                    defenseDivisor: _rules.SelfDestructDefenseDivisor);

                Target.Attributes.ReceiveDamage(damage);
                _emitter?.Emit(new DamageDealt(Target.Name, damage, eff,
                    Target.Attributes.HP, Target.Attributes.MaxHP, isCrit));

                // User faints unconditionally (already handled miss → faint above)
                Source.Attributes.ReceiveDamage(Source.Attributes.HP);
                break;
            }

            case DamageCategory.SuperFang:
                damage = DamageCalculator.CalculateSuperFangDamage(Target);
                Target.Attributes.ReceiveDamage(damage);
                _emitter?.Emit(new DamageDealt(Target.Name, damage, 1.0,
                    Target.Attributes.HP, Target.Attributes.MaxHP));
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

        EndRampageIfDone();   // confuse the user if this was the rampage's last turn

        return Task.CompletedTask;
    }

    private void TryApplyStatus(Attack attack)
    {
        if (attack.StatusEffect == StatusCondition.None) return;
        if (Target.Status != StatusCondition.None) return;
        if (!Target.IsAlive()) return;

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
        if (_rng.Next(1, 101) > chance) return;

        Target.Status = attack.StatusEffect;

        if (attack.StatusEffect == StatusCondition.Sleep)
            Target.SleepTurns = _rules.RollSleepTurns();

        _emitter?.Emit(new StatusApplied(Target.Name, attack.StatusEffect));
    }

    private void TryApplyStatEffect(Attack attack)
    {
        var se = attack.StatEffect;
        if (se == null) return;
        if (!Target.IsAlive() && se.Target == StageTarget.Foe) return;

        Creature affected = se.Target == StageTarget.Self ? Source : Target;

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
        if (_rng.Next(1, 101) > chance) return;

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
                    int chance = _rules.GetSecondaryEffectChance(attack, SecondaryEffectKind.Flinch);
                    if (_rng.Next(1, 101) <= chance)
                        Target.IsFlinched = true;
                }
                break;

            case MoveEffect.LeechSeed:
                if (Target.IsAlive() && !_rules.CanBeLeechSeeded(Target))
                    _emitter?.Emit(new MoveHadNoEffect(Target.Name, attack.Name ?? ""));   // Grass-types are immune
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
                _emitter?.Emit(new CoinsScattered(Source.Name, _rules.PayDayCoinMultiplier * Source.Level));
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
                        Target.DisabledMove          = locked;
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
                if (Target.IsAlive()
                    && Source.LastDamageTaken > 0
                    && Source.LastDamageType is DamageType.Normal or DamageType.Fighting)
                {
                    int countered = Source.LastDamageTaken * 2;
                    Target.Attributes.ReceiveDamage(countered);
                    Target.LastDamageTaken = countered;
                    Target.LastDamageType  = attack.DamageType;
                    _emitter?.Emit(new DamageDealt(Target.Name, countered, 1.0,
                        Target.Attributes.HP, Target.Attributes.MaxHP));
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

            case MoveEffect.Heal:
                // Recover / Soft-Boiled: restore a fraction of max HP to the user (capped at max by
                // ReceiveHealing). The heal fraction is gen-variable, so it comes from the rules seam.
                if (Source.IsAlive())
                {
                    int hpBefore = Source.Attributes.HP;
                    int heal     = Math.Max(1, (int)(Source.Attributes.MaxHP * _rules.RecoverHealFraction));
                    Source.Attributes.ReceiveHealing(heal);
                    // Report the amount actually restored (ReceiveHealing caps at max), not the request.
                    _emitter?.Emit(new Healed(Source.Name, Source.Attributes.HP - hpBefore, Source.Attributes.HP));
                }
                break;

            case MoveEffect.Mimic:
                // Gen 1: copy a random move from the target's set; it replaces Mimic for the rest of
                // the battle (Battle restores it on battle end). The user only copies this turn — no
                // damage — then can use the copied move on later turns. Fails if already mimicked or
                // the target has no copyable move.
                if (Target.IsAlive() && _selectedMove != null && Source.MimicWrapper == null)
                {
                    var copyable = Target.MoveSet
                        .Where(m => m.Base.Effect != MoveEffect.Mimic && m.Base.Name != "struggle")
                        .ToList();
                    if (copyable.Count > 0)
                    {
                        var chosen = copyable[_rng.Next(copyable.Count)].Base;
                        Source.MimicWrapper      = _selectedMove;
                        Source.MimicOriginalBase = _selectedMove.Base;
                        _selectedMove.Base       = chosen;
                        _emitter?.Emit(new MimicLearned(Source.Name, chosen.Name ?? ""));
                    }
                }
                break;

            case MoveEffect.Confuse:
                // Confusion is independent of major status (a creature can be both). Gen 1
                // confusion doesn't stack — only applies if the target isn't already confused.
                // EffectChance gates secondary confusion on damaging moves (Psybeam 10%);
                // pure confusion moves (Supersonic, Confuse Ray) have no chance ⇒ always land.
                if (Target.IsAlive() && Target.ConfusedTurns == 0)
                {
                    int chance = _rules.GetSecondaryEffectChance(attack, SecondaryEffectKind.Confuse);
                    if (_rng.Next(1, 101) <= chance)
                    {
                        Target.ConfusedTurns = _rules.RollConfusionTurns();
                        _emitter?.Emit(new ConfusionStarted(Target.Name));
                    }
                }
                break;
        }
    }

    private static int ApplyStageChange(Creature creature, StageStat stat, int delta) => stat switch
    {
        StageStat.Attack   => After(() => creature.Stages.RaiseAttack(delta),   () => creature.Stages.Attack),
        StageStat.Defense  => After(() => creature.Stages.RaiseDefense(delta),  () => creature.Stages.Defense),
        StageStat.Special  => After(() => creature.Stages.RaiseSpecial(delta),  () => creature.Stages.Special),
        StageStat.Speed    => After(() => creature.Stages.RaiseSpeed(delta),    () => creature.Stages.Speed),
        StageStat.Accuracy => After(() => creature.Stages.RaiseAccuracy(delta), () => creature.Stages.Accuracy),
        StageStat.Evasion  => After(() => creature.Stages.RaiseEvasion(delta),  () => creature.Stages.Evasion),
        _ => 0
    };

    private static int After(Action mutate, Func<int> read) { mutate(); return read(); }
}
