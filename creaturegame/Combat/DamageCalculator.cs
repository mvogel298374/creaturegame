using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>An inclusive low–high damage range (equal bounds = an exact amount).</summary>
public readonly record struct DamageRange(int Min, int Max)
{
    public static DamageRange Exact(int amount) => new(amount, amount);
}

public static class DamageCalculator
{
    /// <summary>Reflect (physical) / Light Screen (special) double the defender's defensive stat while up (a crit
    /// ignores it, handled inside the formula). The one place this is decided — the live attack and the Dev Mode
    /// range estimate both read it.</summary>
    public static int ScreenMultiplier(Creature defender, Attack move, IBattleRules rules) =>
        (move.AttackType == AttackType.Physical && defender.Battle.HasReflect)
        || (move.AttackType == AttackType.Special && defender.Battle.HasLightScreen)
            ? rules.ScreenDefenseMultiplier
            : 1;

    /// <summary>Dev Mode's non-crit low–high for a move, or null; GENERATION_SEAMS.md → Damage formula.</summary>
    public static DamageRange? EstimateRange(
        Creature attacker,
        Creature defender,
        Attack move,
        ITypeChart typeChart,
        IBattleRules rules,
        BattleSide side
    )
    {
        switch (move.DamageCategory)
        {
            case DamageCategory.Fixed:
                return DamageRange.Exact(move.FixedDamageValue ?? 1);
            case DamageCategory.LevelBased:
                return DamageRange.Exact(CalculateLevelBasedDamage(attacker));
            case DamageCategory.OHKO:
                return DamageRange.Exact(defender.Attributes.HP);
            case DamageCategory.SuperFang:
                return DamageRange.Exact(CalculateSuperFangDamage(defender));
            case DamageCategory.Psywave:
            {
                var (min, max) = rules.PsywaveDamageRange(attacker, side);
                return new DamageRange(min, max);
            }
            case DamageCategory.Standard:
            case DamageCategory.Drain:
            case DamageCategory.SelfDestruct:
            {
                if (move.BaseDamage <= 0)
                    return null;
                int divisor =
                    move.DamageCategory == DamageCategory.SelfDestruct
                        ? rules.SelfDestructDefenseDivisor
                        : 1;
                int screen = ScreenMultiplier(defender, move, rules);
                int unvaried = ComputeDamage(
                    attacker,
                    defender,
                    move,
                    typeChart,
                    rules,
                    false,
                    divisor,
                    screen
                );
                var (lo, hi) = rules.DamageVarianceRange;
                return new DamageRange(
                    ApplyDamageRoll(unvaried, lo, rules),
                    ApplyDamageRoll(unvaried, hi, rules)
                );
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Calculates damage, rolls for a critical hit, and sets <paramref name="isCrit"/>.
    /// Stat stages and generation-specific rules are delegated to <paramref name="rules"/>.
    /// </summary>
    public static int CalculateDamage(
        Creature attacker,
        Creature defender,
        Attack move,
        ITypeChart typeChart,
        IBattleRules rules,
        out bool isCrit,
        IRandomSource? rng = null,
        int defenseDivisor = 1,
        int screenDefenseMultiplier = 1
    )
    {
        if (move.BaseDamage == 0)
        {
            isCrit = false;
            return 0;
        }

        isCrit =
            (rng ?? SystemRandomSource.Instance).NextDouble() < rules.GetCritChance(attacker, move);

        int unvaried = ComputeDamage(
            attacker,
            defender,
            move,
            typeChart,
            rules,
            isCrit,
            defenseDivisor,
            screenDefenseMultiplier
        );
        return RollDamage(unvaried, rules);
    }

    private static int RollDamage(int unvaried, IBattleRules rules) =>
        SkipsDamageRoll(unvaried, rules)
            ? unvaried
            : ApplyDamageRoll(unvaried, rules.RollDamageVariance(), rules);

    private static int ApplyDamageRoll(int unvaried, int roll, IBattleRules rules) =>
        SkipsDamageRoll(unvaried, rules) ? unvaried : unvaried * roll / rules.DamageVarianceDivisor;

    private static bool SkipsDamageRoll(int unvaried, IBattleRules rules) =>
        rules.DamageRollSkippedBelowTwo && unvaried < 2;

    /// <summary>The AI's no-crit, no-roll, unscreened estimate; GENERATION_SEAMS.md → Damage formula.</summary>
    public static int EstimateDamage(
        Creature attacker,
        Creature defender,
        Attack move,
        ITypeChart typeChart,
        IBattleRules? rules = null
    ) =>
        move.BaseDamage == 0
            ? 0
            : ComputeDamage(
                attacker,
                defender,
                move,
                typeChart,
                rules ?? Gen1BattleRules.Instance,
                isCrit: false,
                defenseDivisor: 1,
                screenDefenseMultiplier: 1
            );

    /// <summary>The damage before the roll, in integer steps; GENERATION_SEAMS.md → Damage formula.</summary>
    private static int ComputeDamage(
        Creature attacker,
        Creature defender,
        Attack move,
        ITypeChart typeChart,
        IBattleRules rules,
        bool isCrit,
        int defenseDivisor,
        int screenDefenseMultiplier
    )
    {
        int damage = ComputeBaseDamage(
            attacker,
            defender,
            move.AttackType,
            move.BaseDamage,
            rules,
            isCrit,
            defenseDivisor,
            screenDefenseMultiplier
        );

        if (attacker.Type1 == move.DamageType || attacker.Type2 == move.DamageType)
            damage = (int)(damage * rules.StabMultiplier);
        if (defender.Type1 is { } type1)
            damage = (int)(damage * typeChart.GetMultiplier(move.DamageType, type1));
        if (defender.Type2 is { } type2)
            damage = (int)(damage * typeChart.GetMultiplier(move.DamageType, type2));
        return damage;
    }

    private static int ComputeBaseDamage(
        Creature attacker,
        Creature defender,
        AttackType attackType,
        int power,
        IBattleRules rules,
        bool isCrit,
        int defenseDivisor,
        int screenDefenseMultiplier
    )
    {
        int attackStat = rules.GetOffensiveStat(attacker, attackType);
        int defenseStat = rules.GetDefensiveStat(defender, attackType);

        if (!isCrit || !rules.CritIgnoresStatStages)
        {
            attackStat = StagedStats.Apply(
                attackStat,
                attacker.Battle.Stages.Of(rules.OffensiveStage(attackType)),
                rules
            );
            defenseStat = StagedStats.Apply(
                defenseStat,
                defender.Battle.Stages.Of(rules.DefensiveStage(attackType)),
                rules
            );

            // Inside the non-crit block, so a crit ignores the screen like stages and Burn.
            defenseStat *= screenDefenseMultiplier;

            if (attackType == AttackType.Physical && attacker.Battle.Status == StatusCondition.Burn)
                attackStat = Math.Max(rules.MinStatValue, attackStat / rules.BurnAttackDivisor);
        }

        (attackStat, defenseStat) = ScaleDamageStats(attackStat, defenseStat, rules);
        defenseStat = Math.Max(1, defenseStat / defenseDivisor);
        attackStat = Math.Max(1, attackStat);

        int level = isCrit ? attacker.Level * rules.CritLevelMultiplier : attacker.Level;
        int damage = (2 * level / 5 + 2) * power * attackStat / defenseStat / 50;
        if (isCrit)
            damage *= rules.CritDamageMultiplier;
        return Math.Min(damage, rules.MaxBaseDamage) + 2;
    }

    // GENERATION_SEAMS.md §2 (Staged stats).
    private static (int Attack, int Defense) ScaleDamageStats(
        int attack,
        int defense,
        IBattleRules rules
    )
    {
        int limit = rules.DamageStatScaleThreshold;
        while (attack >= limit || defense >= limit)
        {
            attack = Math.Max(rules.MinStatValue, attack / 4);
            defense /= 4;
            if (rules.ScreenedStatWrapsAfterScaling)
                return (attack, defense % limit);
        }
        return (attack, defense);
    }

    /// <summary>
    /// Convenience overload for callers that don't need the crit-out — the damage-only tests
    /// (STAB/stage/burn/stat-selection asserts that don't pin crit). Defaults <c>rules</c> to the
    /// Gen 1 singleton and discards the crit result; production code uses the crit-out overload above.
    /// </summary>
    public static int CalculateDamage(
        Creature attacker,
        Creature defender,
        Attack move,
        ITypeChart typeChart,
        IBattleRules? rules = null
    ) =>
        CalculateDamage(
            attacker,
            defender,
            move,
            typeChart,
            rules ?? Gen1BattleRules.Instance,
            out _
        );

    /// <summary>The confusion self-hit; GENERATION_SEAMS.md §2 (Confusion self-hit).</summary>
    public static int CalculateConfusionDamage(
        Creature user,
        Creature? foe,
        IBattleRules? rules = null
    )
    {
        var r = rules ?? Gen1BattleRules.Instance;
        var reflectHolder = r.ConfusionSelfHitUsesFoeReflect ? foe : user;
        int screen = reflectHolder?.Battle.HasReflect == true ? r.ScreenDefenseMultiplier : 1;
        int damage = ComputeBaseDamage(
            user,
            user,
            AttackType.Physical,
            r.ConfusionSelfHitPower,
            r,
            isCrit: false,
            defenseDivisor: 1,
            screenDefenseMultiplier: screen
        );
        if (r.ConfusionSelfHitRollsVariance)
            damage = RollDamage(damage, r);
        return Math.Max(1, damage);
    }

    /// <summary>Super Fang / Guillotine-variant: deals exactly half the target's current HP.</summary>
    public static int CalculateSuperFangDamage(Creature defender) =>
        Math.Max(1, defender.Attributes.HP / 2);

    /// <summary>Seismic Toss / Night Shade: damage equals attacker's level.</summary>
    public static int CalculateLevelBasedDamage(Creature attacker) => attacker.Level;

    public static double GetTypeEffectiveness(
        DamageType moveType,
        DamageType? targetType1,
        DamageType? targetType2,
        ITypeChart typeChart
    )
    {
        double multiplier = 1.0;
        if (targetType1.HasValue)
            multiplier *= typeChart.GetMultiplier(moveType, targetType1.Value);
        if (targetType2.HasValue)
            multiplier *= typeChart.GetMultiplier(moveType, targetType2.Value);
        return multiplier;
    }
}
