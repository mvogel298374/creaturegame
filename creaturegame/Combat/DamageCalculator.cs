using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public static class DamageCalculator
{
    /// <summary>
    /// Calculates Gen 1 damage using the authentic RBY formula.
    /// Type effectiveness is delegated to the provided <paramref name="typeChart"/>,
    /// making this method generation-agnostic.
    /// </summary>
    public static int CalculateGen1Damage(Creature attacker, Creature defender, Attack move, ITypeChart typeChart)
    {
        // Gen 1 Damage Formula:
        // Damage = ((((2 * Level / 5 + 2) * Attack * Power / Defense) / 50) + 2) * STAB * Type * Random / 255
        if (move.BaseDamage == 0) return 0;

        int attackStat = move.AttackType == AttackType.Physical ? attacker.Attributes.Attack : attacker.Attributes.Special;
        int defenseStat = move.AttackType == AttackType.Physical ? defender.Attributes.Defense : defender.Attributes.Special;

        double baseDamage = (((2.0 * attacker.Level / 5.0 + 2.0) * attackStat * move.BaseDamage / defenseStat) / 50.0) + 2.0;

        // STAB (Same Type Attack Bonus)
        double stab = (attacker.Type1 == move.DamageType || attacker.Type2 == move.DamageType) ? 1.5 : 1.0;

        // Type effectiveness via injected chart (swappable per generation)
        double typeEffectiveness = GetTypeEffectiveness(move.DamageType, defender.Type1, defender.Type2, typeChart);

        // Random factor (217–255) / 255 — authentic Gen 1 range
        double random = Random.Shared.Next(217, 256) / 255.0;

        int finalDamage = (int)(baseDamage * stab * typeEffectiveness * random);
        return finalDamage;
    }

    public static double GetTypeEffectiveness(DamageType moveType, DamageType? targetType1, DamageType? targetType2, ITypeChart typeChart)
    {
        double multiplier = 1.0;
        if (targetType1.HasValue) multiplier *= typeChart.GetMultiplier(moveType, targetType1.Value);
        if (targetType2.HasValue) multiplier *= typeChart.GetMultiplier(moveType, targetType2.Value);
        return multiplier;
    }
}
