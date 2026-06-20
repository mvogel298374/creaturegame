using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public static class DamageCalculator
{
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
        double variance = rules.RollDamageVariance();

        return ComputeDamage(
            attacker,
            defender,
            move,
            typeChart,
            rules,
            isCrit,
            variance,
            defenseDivisor,
            screenDefenseMultiplier
        );
    }

    /// <summary>
    /// Deterministic, no-RNG damage estimate for AI move scoring. Assumes no critical hit and pins the damage
    /// variance to 1.0 (no roll) — variance is a single multiplicative factor shared by every move, so it
    /// cancels out when <i>ranking</i> moves; fixing it at 1.0 keeps no gen-variable variance constant leaking
    /// into the estimate. Honours the current stat stages, Burn, STAB and the type chart exactly like the live
    /// calculator (Reflect/Light Screen are ignored — the AI doesn't model the foe's screens). Returns 0 for
    /// a non-damaging move; the special damage categories (Fixed, LevelBased, OHKO, …) are resolved by the
    /// caller, since this covers only the Standard power-based formula.
    /// </summary>
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
                variance: 1.0,
                defenseDivisor: 1,
                screenDefenseMultiplier: 1
            );

    /// <summary>
    /// The pure Gen 1 damage formula given an already-decided crit and variance roll. Shared by the live
    /// <see cref="CalculateDamage(Creature, Creature, Attack, ITypeChart, IBattleRules, out bool, IRandomSource?, int, int)"/>
    /// (which rolls crit + variance) and the deterministic <see cref="EstimateDamage"/> (which pins them), so
    /// both stay in lock-step with no duplicated formula. Caller guarantees <c>move.BaseDamage &gt; 0</c>.
    /// </summary>
    private static int ComputeDamage(
        Creature attacker,
        Creature defender,
        Attack move,
        ITypeChart typeChart,
        IBattleRules rules,
        bool isCrit,
        double variance,
        int defenseDivisor,
        int screenDefenseMultiplier
    )
    {
        // Stat selection is delegated to rules — Gen 1 uses Special for both special offence
        // and defence; Gen 2+ will return SpAtk / SpDef respectively.
        int attackStat = rules.GetOffensiveStat(attacker, move.AttackType);
        int defenseStat = rules.GetDefensiveStat(defender, move.AttackType);

        if (!isCrit || !rules.CritIgnoresStatStages)
        {
            // Apply stat stage multipliers (skipped for crits in Gen 1).
            double atkStageMult = rules.GetStatMultiplier(
                move.AttackType == AttackType.Physical
                    ? attacker.Battle.Stages.Attack
                    : attacker.Battle.Stages.Special
            );
            double defStageMult = rules.GetStatMultiplier(
                move.AttackType == AttackType.Physical
                    ? defender.Battle.Stages.Defense
                    : defender.Battle.Stages.Special
            );

            attackStat = (int)(attackStat * atkStageMult);
            defenseStat = (int)(defenseStat * defStageMult);

            // Reflect / Light Screen multiply the defensive stat while up. Inside the non-crit block
            // so a crit ignores the screen (Gen 1), exactly like stat stages and Burn below.
            defenseStat *= screenDefenseMultiplier;

            // Burn halves physical Attack (skipped on crit path in Gen 1).
            if (
                move.AttackType == AttackType.Physical
                && attacker.Battle.Status == StatusCondition.Burn
            )
                attackStat /= 2;
        }

        // Guard against zero defense (edge case with very low stats + negative stages).
        // defenseDivisor carries gen-variable move quirks (Self-Destruct/Explosion halve Defense)
        // so the divisor stays on IBattleRules instead of being hardcoded at the call site.
        defenseStat = Math.Max(1, defenseStat / defenseDivisor);
        attackStat = Math.Max(1, attackStat);

        double baseDamage =
            (
                ((2.0 * attacker.Level / 5.0 + 2.0) * attackStat * move.BaseDamage / defenseStat)
                / 50.0
            ) + 2.0;

        double stab =
            (attacker.Type1 == move.DamageType || attacker.Type2 == move.DamageType)
                ? rules.StabMultiplier
                : 1.0;
        double typeEffectiveness = GetTypeEffectiveness(
            move.DamageType,
            defender.Type1,
            defender.Type2,
            typeChart
        );
        double critMult = isCrit ? rules.CritMultiplier : 1.0;

        return (int)(baseDamage * stab * typeEffectiveness * critMult * variance);
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

    // Confusion self-damage is a typeless physical hit at this fixed power (gen-invariant: 40).
    private const int ConfusionBasePower = 40;

    public static int CalculateConfusionDamage(Creature attacker, IBattleRules? rules = null)
    {
        var r = rules ?? Gen1BattleRules.Instance;
        // Stat reads go through the rules seam (physical Attack vs Defense in Gen 1; a split-
        // Special gen would still resolve Physical → Attack/Defense here) rather than touching
        // Attributes directly.
        int attackStat = r.GetOffensiveStat(attacker, AttackType.Physical);
        int defenseStat = Math.Max(1, r.GetDefensiveStat(attacker, AttackType.Physical));
        double raw =
            ((2.0 * attacker.Level / 5.0 + 2.0) * attackStat * ConfusionBasePower / defenseStat)
                / 50.0
            + 2.0;
        return Math.Max(1, (int)(raw * r.RollDamageVariance()));
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
