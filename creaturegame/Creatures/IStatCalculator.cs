namespace creaturegame.Creatures;

/// <summary>
/// The quality band a caller requests when rolling a creature's individual values. It is *intent* — the
/// mapping to actual DV/IV ranges is generation-specific and lives on the <see cref="IStatCalculator"/>
/// implementation (Gen 1: <see cref="Poor"/> 0–7, <see cref="Average"/> 0–15, <see cref="High"/> 8–15,
/// <see cref="Superb"/> = each value a 50% chance at the top percentile band else an ordinary roll,
/// <see cref="Perfect"/> = max). Enemy strength tiers pick a quality; <see cref="Average"/> is the ordinary
/// roll (the player's). Quality is always passed explicitly — there is no implicit "just random" overload.
/// </summary>
public enum DvQuality
{
    Poor,
    Average,
    High,

    /// <summary>A strong-but-not-flawless roll: each individual value has a 50% chance to land in the top
    /// percentile band (Gen 1: the 80–100% band, DV 12–15), otherwise an ordinary roll. So about half the values
    /// come out near-max and the rest are luck of the draw — above <see cref="High"/> in expectation without the
    /// guaranteed floor of <see cref="Perfect"/>. Used for the boss-catch pickup (a beaten boss you add to the
    /// party: notably strong, but not a min-maxed clone of the encounter you fought).</summary>
    Superb,
    Perfect,
}

/// <summary>Stat formulas, individual-value rolls and the training award; see GENERATION_SEAMS.md → Stat calculation.</summary>
public interface IStatCalculator
{
    /// <summary>Calculates the HP stat; per-generation formula in GENERATION_SEAMS.md → Stat calculation.</summary>
    int CalculateHP(int baseStat, int dv, int statExp, int level);

    /// <summary>Calculates any non-HP stat; per-generation formula in GENERATION_SEAMS.md → Stat calculation.</summary>
    int CalculateOtherStat(int baseStat, int dv, int statExp, int level);

    /// <summary>Rolls a creature's individual values in place at <paramref name="quality"/>; the Gen 1 bands are in <c>Gen1StatCalculator</c>.</summary>
    void RandomiseDvs(Creature creature, DvQuality quality);

    /// <summary>Adds the victor's training, each yield floored by <paramref name="yieldDivisor"/>; realized on recompute.</summary>
    void AwardStatExp(Creature victor, Creature defeated, int yieldDivisor);
}
