namespace creaturegame.Creatures;

public class Attributes
{
    public int Attack { get; set; } = 50;
    public int Defense { get; set; } = 50;
    public int Special { get; set; } = 50;
    public int HP { get; set; } = 100;
    public int MaxHP { get; set; } = 100;
    public int Speed { get; set; } = 50;

    public override string ToString()
    {
        return $@"ATK: {this.Attack}, DEF: {this.Defense}, SPEC: {this.Special}, HP: {this.HP}/{this.MaxHP}, SPD: {this.Speed}";
    }

    public void ReceiveDamage(int damage)
    {
        this.HP -= damage;
        if (this.HP <= 0)
            this.HP = 0;
    }

    public void ReceiveHealing(int healing)
    {
        this.HP += healing;
        if (this.HP >= this.MaxHP)
            this.HP = this.MaxHP;
    }
}

/// <summary>An immutable snapshot of a creature's stat totals — carried by the level-up event so the
/// client can show the Gen 1 stat-growth panel without reaching into mutable <see cref="Attributes"/>.</summary>
public record StatBlock(int MaxHp, int Attack, int Defense, int Special, int Speed)
{
    /// <summary>Per-stat difference (<c>this − other</c>) — used to report the gains from a level-up.</summary>
    public StatBlock Minus(StatBlock other) =>
        new(
            MaxHp - other.MaxHp,
            Attack - other.Attack,
            Defense - other.Defense,
            Special - other.Special,
            Speed - other.Speed
        );
}
