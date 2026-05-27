using System.ComponentModel.DataAnnotations.Schema;
using creaturegame.Creatures;

namespace creaturegame.Attacks;

public class Attack
{
    public int Id { get; set; }
    public int BaseDamage { get; set; } = 10;
    public AttackType AttackType { get; set; } = AttackType.Physical;

    public DamageType DamageType { get; set; } = DamageType.Normal;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int Accuracy { get; set; } = 100;
    public int PowerPointsMax { get; set; } = 30;
    // Gen 1 Properties
    public int Priority { get; set; } = 0;
    public int? EffectChance { get; set; }
    public StatusCondition StatusEffect { get; set; } = StatusCondition.None;
    public bool IsHighCrit { get; set; } = false;

    // Stat-stage effect — four nullable columns; StatEffect computed from them
    public StageStat?   StatEffectStat    { get; set; }
    public int?         StatEffectDelta   { get; set; }
    public StageTarget? StatEffectTarget  { get; set; }
    public int?         StatEffectChance  { get; set; }

    [NotMapped]
    public StatEffect? StatEffect => StatEffectStat.HasValue
        ? new StatEffect(StatEffectStat.Value, StatEffectDelta ?? 0,
                         StatEffectTarget ?? StageTarget.Self, StatEffectChance ?? 100)
        : null;

    // Special non-stat move effect (Haze, Flinch, etc.)
    public MoveEffect Effect { get; set; } = MoveEffect.None;
    
    public Attack()
    {
    }
    
    public Attack(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public override string ToString()
    {
        return $"Name: {Name}, Damage: {BaseDamage}, DamageType: {AttackType}, Description: {Description}";
    }
}