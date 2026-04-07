using creaturegame.Attacks;
using creaturegame.Creature;

namespace creaturegame.Combat;

public static class DamageCalculator
{
    public static int CalculateGen1Damage(Creature.Creature attacker, Creature.Creature defender, Attack move)
    {
        // Gen 1 Damage Formula:
        // Damage = ((((2 * Level / 5 + 2) * Attack * Power / Defense) / 50) + 2) * STAB * Type * Random / 255

        if (move.BaseDamage == 0) return 0;

        int level = attacker.Attributes.HP > 0 ? 1 : 1; // Level is needed, currently default to 1 if not accessible easily, but Creature has it protected.
        // We'll use a hack to get level or make it public.
        
        // Base Damage calculation
        int attackStat = move.AttackType == AttackType.Physical ? attacker.Attributes.Attack : attacker.Attributes.Special;
        int defenseStat = move.AttackType == AttackType.Physical ? defender.Attributes.Defense : defender.Attributes.Special;

        // Note: For now we assume attacker.Level is accessible (need to check access modifier)
        // Since I'm writing this code, I'll make Level public in Creature if needed.
        
        double baseDamage = (((2.0 * attacker.Level / 5.0 + 2.0) * attackStat * move.BaseDamage / defenseStat) / 50.0) + 2.0;

        // STAB (Same Type Attack Bonus)
        double stab = 1.0;
        if (attacker.Type1 == move.DamageType || attacker.Type2 == move.DamageType)
        {
            stab = 1.5;
        }

        // Type Effectiveness
        double typeEffectiveness = GetTypeEffectiveness(move.DamageType, defender.Type1, defender.Type2);

        // Random factor (217-255) / 255
        double random = Random.Shared.Next(217, 256) / 255.0;

        int finalDamage = (int)(baseDamage * stab * typeEffectiveness * random);

        return finalDamage;
    }

    public static double GetTypeEffectiveness(DamageType moveType, DamageType? targetType1, DamageType? targetType2)
    {
        double multiplier = 1.0;
        if (targetType1.HasValue) multiplier *= TypeChart.GetMultiplier(moveType, targetType1.Value);
        if (targetType2.HasValue) multiplier *= TypeChart.GetMultiplier(moveType, targetType2.Value);
        return multiplier;
    }
}

public static class TypeChart
{
    // Simplified multiplier for now, will expand later
    public static double GetMultiplier(DamageType moveType, DamageType targetType)
    {
        // TODO: Implement full 18x18 matrix
        return 1.0;
    }
}
