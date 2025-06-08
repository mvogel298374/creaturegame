namespace creaturegame.Attacks;

public class Attack(string name, string description)
{
    public int BaseDamage { get; set; } = 10;
    private DamageType DamageType { get; set; } = DamageType.Physical;
    private string Name { get; set; } = name;
    private string Description { get; set; } = description;
    private int Accuracy { get; set; } = 100;
    public int PowerPointsMax { get; set; } = 30;
    public int PowerPointsCurrent { get; set; } = 30;
    public double CriticalChance { get; set; } = 0.05;
    public double DamageVariance { get; set; } = 0.10;
    public int Cooldown { get; set; } = 0;
    public int Weight { get; set; } = 500;
    

    public override string ToString()
    {
        return $"Name: {Name}, Damage: {BaseDamage}, DamageType: {DamageType}, Description: {Description}";
    }
}