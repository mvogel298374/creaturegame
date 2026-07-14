using creaturegame.Combat;

namespace creaturegame.Web.Battle;

/// <summary>
/// The boss-catch policy behind the run's injected boss-catch supplier (<c>RunDirector</c>'s
/// <c>Func&lt;BossCatchContext, IRandomSource, Task&lt;Creature?&gt;&gt;</c>) — the boss channel's sibling of
/// <see cref="DraftCalculator"/>: run-layer roguelite tuning, not a battle seam, and <c>internal static</c> + pure
/// so the gate is unit-testable without a DB (the creature <em>build</em> lives in <see cref="EncounterFactory"/>,
/// which owns the queries).
///
/// <para><see cref="ShouldOffer"/> is the whole gate: a small <see cref="CatchPercent"/>% chance per Boss win.
/// There is no cadence and no pool — unlike the draft, the boss catch fires off a single Boss win with the boss
/// you just beat as the only candidate, so the win reward/XP is already applied and the catch is pure upside. The
/// percentage is provisional balance tuning — the tests assert the <em>rule</em> (the roll boundary), not the
/// exact number.</para>
/// </summary>
internal static class BossCatchCalculator
{
    /// <summary>The share of Boss wins that offer a catch (the small n% roll). Deliberately low — a Boss win is
    /// infrequent (one biome apex), and the offered creature is a full boss-tier species, so the catch is a rare
    /// bonus rather than a reliable roster-filler.</summary>
    public const int CatchPercent = 20;

    /// <summary>The complete offer gate: true iff this Boss win should raise a boss-catch offer — a single n% roll,
    /// drawn only when this method is called (the director only calls it on a Boss win, so a wild/elite win never
    /// perturbs the seeded stream).</summary>
    public static bool ShouldOffer(IRandomSource rng) => rng.Next(100) < CatchPercent;
}
