using System.Net.NetworkInformation;

namespace creaturegame.Creature;

public class Attributes
{
    private int PhysicalAttack { get; set; } = 50;
    private int SpecialAttack { get; set; } = 50;
    private int PhysicalDefense { get; set; } = 50;
    private int SpecialDefense { get; set; } = 50;
    private int Health { get; set; } = 100;
    private int Speed { get; set; } = 50;


    public override string ToString()
    {
        return $@"ATK: {this.PhysicalAttack}, DEF: {this.PhysicalDefense}, Sp. ATK: {this.SpecialAttack}, Sp. DEF: {this.SpecialDefense}, HP: {this.Health}, SPD: {this.Speed}";
    }

    public void SetAttributesByCreatureType(CreatureType creature)
    {
        //TODO some magic here to get the creaturetype base stats out of a db etc
        switch (creature)
        {
            case CreatureType.Dragon:
            {
                PhysicalAttack = 100;
                SpecialAttack = 80;
                PhysicalDefense = 60;
                SpecialDefense = 60;
                Health = 200;
                Speed = 100;
                break;
            }
            case CreatureType.Undefined:
            default:
            {
                PhysicalAttack = 50;
                SpecialAttack = 50;
                PhysicalDefense = 50;
                SpecialDefense = 50;
                Health = 100;
                Speed = 50;
                break;
            } 
        }
    }
    
    public void ReceiveDamage(int damage)
    {
        this.Health -= damage;
        if (this.Health <= 0)
            this.Health = 0;
    }

    public void ReceiveHealing(int healing)
    {
        var currentHealth = GetCurrentHealth();
        this.Health += healing;
        if (this.Health >= currentHealth)
            this.Health = currentHealth;
    }

    public int GetCurrentHealth()
    {
        return this.Health;
    }

    public int GetSpeed()
    {
        return this.Speed;
    }

}

