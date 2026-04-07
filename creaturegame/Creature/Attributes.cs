using System.Net.NetworkInformation;

namespace creaturegame.Creature;

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

    public void SetAttributesByCreatureType(CreatureType creature)
    {
        //TODO some magic here to get the creaturetype base stats out of a db etc
        switch (creature)
        {
            case CreatureType.Dragon:
            {
                Attack = 100;
                Special = 80;
                Defense = 60;
                HP = 200;
                MaxHP = 200;
                Speed = 100;
                break;
            }
            case CreatureType.Undefined:
            default:
            {
                Attack = 50;
                Special = 50;
                Defense = 50;
                HP = 100;
                MaxHP = 100;
                Speed = 50;
                break;
            } 
        }
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

    public int GetCurrentHealth()
    {
        return this.HP;
    }

    public int GetSpeed()
    {
        return this.Speed;
    }
}

