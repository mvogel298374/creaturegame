using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// Roguelite run-tuning — the "game rules" dials that shape a run's <em>balance</em>, deliberately kept
/// SEPARATE from the Gen-1 battle seam (<see cref="IBattleRules"/>). That separation is the whole point:
/// <see cref="IBattleRules"/> is faithful Gen-1 <em>mechanics</em> (never touched for balance), while
/// <c>RunRules</c> is layered game-balance a run may tune or a UI may expose as sliders — it never changes a
/// Gen-1 formula, only scales its result. Today it carries the run XP curve; other roguelite knobs (drop
/// rates, gold curve — currently in the web <c>RewardCalculator</c>) can gather here as they graduate from
/// magic constants to tunable rules. <see cref="Default"/> is a no-op that leaves pure Gen-1 pacing intact.
/// </summary>
public sealed class RunRules
{
    /// <summary>XP award multiplier at the low end of the level range (level 1). 1.0 = untouched Gen-1.</summary>
    public double XpMultiplierEarly { get; init; } = 1.0;

    /// <summary>XP award multiplier at the top of the level range (<see cref="Creature.MaxLevel"/>). 1.0 = untouched Gen-1.</summary>
    public double XpMultiplierLate { get; init; } = 1.0;

    /// <summary>
    /// Innate party Exp-Share (roguelite Exp-All): the fraction of the <em>active</em> creature's XP award that
    /// each <em>living bench</em> member also earns from a win, so the whole roster keeps pace and stays swappable.
    /// The active creature is always paid in full — this only tops up the bench. <c>0.0</c> = off (only the active
    /// earns, legacy Gen-1-ish behaviour — the property <b>default</b>, so <see cref="Default"/> and every unopted
    /// caller stay a pure no-op); <c>0.5</c> = each bench member earns half the lead's award (the live run's
    /// <c>RunTuning</c> value); <c>1.0</c> = full XP to everyone. A run-balance dial, deliberately outside the
    /// Gen-1 seam. Fainted members are excluded regardless (a fainted participant earns nothing, per Gen 1). Only
    /// fires when a party is threaded into <see cref="Battle"/>; direct single-creature callers pass no party, so
    /// it never applies there.
    /// </summary>
    public double BenchXpShare { get; init; } = 0.0;

    /// <summary>Gen-1-faithful default: no XP scaling at any level (multiplier 1.0 throughout) — so every caller
    /// that doesn't opt in (tests, the legacy chain, direct <see cref="Battle"/> use) runs pure Gen-1 pace.</summary>
    public static readonly RunRules Default = new();

    /// <summary>
    /// The XP award multiplier for a winner at <paramref name="level"/> — a soft, <em>linear-by-level</em> ramp
    /// from <see cref="XpMultiplierEarly"/> (level 1) to <see cref="XpMultiplierLate"/> (<see cref="Creature.MaxLevel"/>),
    /// clamped to that range. Deliberately a gentle slope, never a sharp step: with the anchors increasing, low
    /// levels (already fast under Gen-1's cheap early thresholds) stay near their natural pace instead of
    /// jumping several levels at once, while the glacial high-level grind gets the bigger lift. Keyed on the
    /// winner's own level, so the boost tracks the creature the player is actually raising.
    /// </summary>
    public double XpMultiplierForLevel(int level)
    {
        int max = Creature.MaxLevel;
        double t = max <= 1 ? 0.0 : Math.Clamp((level - 1) / (double)(max - 1), 0.0, 1.0);
        return XpMultiplierEarly + (XpMultiplierLate - XpMultiplierEarly) * t;
    }
}
