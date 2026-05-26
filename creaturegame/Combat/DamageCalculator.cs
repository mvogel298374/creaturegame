using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public static class DamageCalculator
{
    /// <summary>
    /// Calculates damage, rolls for a critical hit, and sets <paramref name="isCrit"/>.
    /// Stat stages and generation-specific rules are delegated to <paramref name="rules"/>.
    /// </summary>
    public static int CalculateDamage(Creature attacker, Creature defender, Attack move,
                                      ITypeChart typeChart, IBattleRules rules, out bool isCrit)
    {
        if (move.BaseDamage == 0) { isCrit = false; return 0; }

        isCrit = Random.Shared.NextDouble() < rules.GetCritChance(attacker, move);

        int attackStat, defenseStat;

        if (isCrit && rules.CritIgnoresStatStages)
        {
            // Gen 1 crits use the computed stats directly — no stage multipliers, no Burn penalty.
            attackStat  = move.AttackType == AttackType.Physical
                ? attacker.Attributes.Attack  : attacker.Attributes.Special;
            defenseStat = move.AttackType == AttackType.Physical
                ? defender.Attributes.Defense : defender.Attributes.Special;
        }
        else
        {
            attackStat  = move.AttackType == AttackType.Physical
                ? attacker.Attributes.Attack  : attacker.Attributes.Special;
            defenseStat = move.AttackType == AttackType.Physical
                ? defender.Attributes.Defense : defender.Attributes.Special;

            // Apply stat stage multipliers
            double atkStageMult = rules.GetStatMultiplier(
                move.AttackType == AttackType.Physical ? attacker.Stages.Attack : attacker.Stages.Special);
            double defStageMult = rules.GetStatMultiplier(
                move.AttackType == AttackType.Physical ? defender.Stages.Defense : defender.Stages.Special);

            attackStat  = (int)(attackStat  * atkStageMult);
            defenseStat = (int)(defenseStat * defStageMult);

            // Burn halves physical Attack (only on non-crit path)
            if (move.AttackType == AttackType.Physical && attacker.Status == StatusCondition.Burn)
                attackStat /= 2;
        }

        // Guard against zero defense (edge case with very low stats + negative stages)
        defenseStat = Math.Max(1, defenseStat);
        attackStat  = Math.Max(1, attackStat);

        double baseDamage = (((2.0 * attacker.Level / 5.0 + 2.0) * attackStat * move.BaseDamage / defenseStat) / 50.0) + 2.0;

        double stab            = (attacker.Type1 == move.DamageType || attacker.Type2 == move.DamageType) ? 1.5 : 1.0;
        double typeEffectiveness = GetTypeEffectiveness(move.DamageType, defender.Type1, defender.Type2, typeChart);
        double critMult        = isCrit ? rules.CritMultiplier : 1.0;
        double random          = rules.RollDamageVariance();

        return (int)(baseDamage * stab * typeEffectiveness * critMult * random);
    }

    /// <summary>Backward-compatible overload — discards the crit result.</summary>
    public static int CalculateDamage(Creature attacker, Creature defender, Attack move,
                                      ITypeChart typeChart, IBattleRules? rules = null)
        => CalculateDamage(attacker, defender, move, typeChart, rules ?? Gen1BattleRules.Instance, out _);

    public static int CalculateConfusionDamage(Creature attacker, IBattleRules? rules = null)
    {
        var r = rules ?? Gen1BattleRules.Instance;
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
