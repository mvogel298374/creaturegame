using System.Collections;

namespace creaturegame.Attacks;

public class Attack
{
    public int Id { get; set; }
    public int BaseDamage { get; set; } = 10;
    public AttackType AttackType { get; set; } = AttackType.Physical;
    
    public DamageType DamageType { get; set; } = DamageType.Normal;
    public string Name { get; set; }
    public string Description { get; set; }
    public int Accuracy { get; set; } = 100;
    public int PowerPointsMax { get; set; } = 30;
    public double CriticalChance { get; set; } = 0.05;
    public double DamageVariance { get; set; } = 0.10;
    public int Cooldown { get; set; } = 0;
    public int Weight { get; set; } = 500;
    
    // New Gen 1 Properties
    public int Priority { get; set; } = 0;
    public int? EffectChance { get; set; }
    
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