using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;

namespace creaturegame.Tests.TestSupport;

public static class TestFighters
{
    public static Creature Fighter(
        string name,
        int hp,
        int attack,
        int speed,
        int level = 50,
        int? defense = null,
        int? tackleId = null
    )
    {
        var c = new Creature(name)
        {
            Level = level,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(level);
        c.Attributes.MaxHP = hp;
        c.Attributes.HP = hp;
        c.Attributes.Attack = attack;
        if (defense is int d)
            c.Attributes.Defense = d;
        c.Attributes.Speed = speed;
        var tackle = new Attack
        {
            Name = "tackle",
            BaseDamage = 40,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
        };
        if (tackleId is int id)
            tackle.Id = id;
        c.AddAttack(tackle);
        return c;
    }
}
