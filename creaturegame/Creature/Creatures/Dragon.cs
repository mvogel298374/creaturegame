using creaturegame.Attacks;

namespace creaturegame.Creature.Creatures;

public class Dragon : Creature
{
    public Dragon()
    {
        InitializeDragon();
    }
    
    public Dragon(string creatureName)
    {
        Name = creatureName;
        InitializeDragon();
    }

    private void InitializeDragon()
    {
        // Use Dragonite stats as a base
        BaseHP = 91;
        BaseAttack = 134;
        BaseDefense = 95;
        BaseSpecial = 100;
        BaseSpeed = 80;
        Type1 = DamageType.Dragon;
        Type2 = DamageType.Flying;
        Level = 50;
        CalculateStats();
    }
}