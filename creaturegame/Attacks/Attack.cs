namespace creaturegame.Attacks;

public class Attack(string name, string description)
{
    public int BaseDamage { get; set; } = 10;
    public DamageType DamageType { get; set; } = DamageType.Physical;
    public string Name { get; set; } = name;
    public string Description { get; set; } = description;

    public override string ToString()
    {
        return $"Name: {Name}, Damage: {BaseDamage}, DamageType: {DamageType}, Description: {Description}";
    }
}