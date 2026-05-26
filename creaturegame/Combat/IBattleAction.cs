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
    private readonly ITypeChart           _typeChart;
    private readonly IBattleRules         _rules;
    private readonly IBattleEventEmitter? _emitter;

    // Null means Struggle — Battle passes null when Source.IsOutOfPP, bypassing IBattleInput.
    private readonly PokemonAttack? _selectedMove;

    public AttackAction(Creature source, Creature target,
                        PokemonAttack? selectedMove, ITypeChart typeChart,
                        IBattleRules? rules = null, IBattleEventEmitter? emitter = null)
    {
        Source        = source;
        Target        = target;
        _typeChart    = typeChart;
        _rules        = rules ?? Gen1BattleRules.Instance;
        _emitter      = emitter;
        _selectedMove = selectedMove;
        Priority      = selectedMove?.Base.Priority ?? 0;
    }

    public Task ExecuteAsync()
    {
        if (!Source.IsAlive()) return Task.CompletedTask;

        bool usingStruggle = _selectedMove == null;
        Attack attackToUse = usingStruggle ? Source.Struggle : _selectedMove!.Base;

        if (!usingStruggle)
            _selectedMove!.PowerPointsCurrent--;

        _emitter?.Emit(new MoveUsed(Source.Name, attackToUse.Name ?? ""));

        // Accuracy check — Struggle always hits; all other moves use the Gen 1 0–255 scale.
        if (!usingStruggle)
        {
            int threshold = _rules.GetHitThreshold(
                attackToUse.Accuracy, Source.Stages.Accuracy, Target.Stages.Evasion);
            if (Random.Shared.Next(_rules.AccuracyRollBound) >= threshold)
            {
                _emitter?.Emit(new MoveMissed(Source.Name, attackToUse.Name ?? ""));
                return Task.CompletedTask;
            }
        }

        // Thaw a frozen target if the move meets the generation's thaw criteria
        if (Target.Status == StatusCondition.Freeze && _rules.CanThawFrozenTarget(attackToUse))
        {
            Target.Status = StatusCondition.None;
            _emitter?.Emit(new StatusCleared(Target.Name, StatusCondition.Freeze));
        }

        int damage = 0;
        if (attackToUse.BaseDamage > 0)
        {
            double effectiveness = DamageCalculator.GetTypeEffectiveness(attackToUse.DamageType, Target.Type1, Target.Type2, _typeChart);
            damage = DamageCalculator.CalculateDamage(Source, Target, attackToUse, _typeChart, _rules, out bool isCrit);
            Target.Attributes.ReceiveDamage(damage);
            _emitter?.Emit(new DamageDealt(Target.Name, damage, effectiveness, Target.Attributes.HP, Target.Attributes.MaxHP, isCrit));
        }

        if (usingStruggle)
        {
            int recoil = _rules.CalculateStruggleRecoil(Source, damage);
            Source.Attributes.ReceiveDamage(recoil);
            _emitter?.Emit(new RecoilDamage(Source.Name, recoil, Source.Attributes.HP));
        }

        TryApplyStatus(attackToUse);

        return Task.CompletedTask;
    }

    private void TryApplyStatus(Attack attack)
    {
        if (attack.StatusEffect == StatusCondition.None) return;
        if (Target.Status != StatusCondition.None) return;
        if (!Target.IsAlive()) return;

        int chance = attack.EffectChance ?? 100;
        if (Random.Shared.Next(1, 101) > chance) return;

        Target.Status = attack.StatusEffect;

        if (attack.StatusEffect == StatusCondition.Sleep)
            Target.SleepTurns = _rules.RollSleepTurns();

        _emitter?.Emit(new StatusApplied(Target.Name, attack.StatusEffect));
    }
}
