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

    // Null means Struggle — Battle passes null when Source.IsOutOfPP, bypassing IBattleInput.
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

        // PP decremented on charge turn only (already consumed; don't double-spend)
        if (!usingStruggle && !isReleaseTurn)
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

        // OHKO: fails (not just misses) when source level < target level (Gen 1 rule)
        if (category == DamageCategory.OHKO && Source.Level < Target.Level)
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
                    damage = DamageCalculator.CalculateDamage(
                        Source, Target, attackToUse, _typeChart, _rules, out isCrit, _rng);
                    Target.Attributes.ReceiveDamage(damage);
                    _emitter?.Emit(new DamageDealt(Target.Name, damage, eff,
                        Target.Attributes.HP, Target.Attributes.MaxHP, isCrit));

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
                // Gen 1: target's Defense (and Special) is halved before damage calculation,
                // then restored — this makes Explosion/Self-Destruct significantly stronger.
                int savedDefense = Target.Attributes.Defense;
                int savedSpecial = Target.Attributes.Special;
                Target.Attributes.Defense = Math.Max(1, Target.Attributes.Defense / 2);
                Target.Attributes.Special = Math.Max(1, Target.Attributes.Special / 2);

                double eff = DamageCalculator.GetTypeEffectiveness(
                    attackToUse.DamageType, Target.Type1, Target.Type2, _typeChart);
                damage = DamageCalculator.CalculateDamage(
                    Source, Target, attackToUse, _typeChart, _rules, out isCrit, _rng);

                Target.Attributes.Defense = savedDefense;
                Target.Attributes.Special = savedSpecial;

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

        return Task.CompletedTask;
    }

    private void TryApplyStatus(Attack attack)
    {
        if (attack.StatusEffect == StatusCondition.None) return;
        if (Target.Status != StatusCondition.None) return;
        if (!Target.IsAlive()) return;

        int chance = attack.EffectChance ?? 100;
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

        if (_rng.Next(1, 101) > se.Chance) return;

        Creature affected = se.Target == StageTarget.Self ? Source : Target;
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
                    int chance = attack.EffectChance ?? 100;
                    if (_rng.Next(1, 101) <= chance)
                        Target.IsFlinched = true;
                }
                break;

            case MoveEffect.LeechSeed:
                if (!Target.HasLeechSeed && Target.IsAlive())
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

            case MoveEffect.Confuse:
                // Confusion is independent of major status (a creature can be both). Gen 1
                // confusion doesn't stack — only applies if the target isn't already confused.
                // EffectChance gates secondary confusion on damaging moves (Psybeam 10%);
                // pure confusion moves (Supersonic, Confuse Ray) have no chance ⇒ always land.
                if (Target.IsAlive() && Target.ConfusedTurns == 0)
                {
                    int chance = attack.EffectChance ?? 100;
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
