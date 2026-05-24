using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public static class DamageCalculator
{
    /// <summary>
    /// Calculates damage using the Gen 1 formula structure.
    /// Random variance and any other generation-specific values are delegated to
    /// <paramref name="rules"/>, making this method generation-agnostic.
    /// </summary>
    public static int CalculateDamage(Creature attacker, Creature defender, Attack move,
                                      ITypeChart typeChart, IBattleRules? rules = null)
    {
        var r = rules ?? Gen1BattleRules.Instance;

        if (move.BaseDamage == 0) return 0;

        int attackStat = move.AttackType == AttackType.Physical ? attacker.Attributes.Attack : attacker.Attributes.Special;
        if (move.AttackType == AttackType.Physical && attacker.Status == StatusCondition.Burn)
            attackStat /= 2;
        int defenseStat = move.AttackType == AttackType.Physical ? defender.Attributes.Defense : defender.Attributes.Special;

        double baseDamage = (((2.0 * attacker.Level / 5.0 + 2.0) * attackStat * move.BaseDamage / defenseStat) / 50.0) + 2.0;

        double stab = (attacker.Type1 == move.DamageType || attacker.Type2 == move.DamageType) ? 1.5 : 1.0;

        double typeEffectiveness = GetTypeEffectiveness(move.DamageType, defender.Type1, defender.Type2, typeChart);

        double random = r.RollDamageVariance();

        int finalDamage = (int)(baseDamage * stab * typeEffectiveness * random);
        return finalDamage;
    }

    public static int CalculateConfusionDamage(Creature attacker, IBattleRules? rules = null)
    {
        var r = rules ?? Gen1BattleRules.Instance;

        // Physical, 40 base power, attacker hits itself with own Attack / own Defense, no type modifier.
        double raw = ((2.0 * attacker.Level / 5.0 + 2.0) * attacker.Attributes.Attack * 40.0 / attacker.Attributes.Defense) / 50.0 + 2.0;
        return Math.Max(1, (int)(raw * r.RollDamageVariance()));
    }

    public static double GetTypeEffectiveness(DamageType moveType, DamageType? targetType1, DamageType? targetType2, ITypeChart typeChart)
    {
        double multiplier = 1.0;
        if (targetType1.HasValue) multiplier *= typeChart.GetMultiplier(moveType, targetType1.Value);
        if (targetType2.HasValue) multiplier *= typeChart.GetMultiplier(moveType, targetType2.Value);
        return multiplier;
    }
}
