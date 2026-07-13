using creaturegame.Combat;

namespace creaturegame.Web.Battle;

/// <summary>
/// The themed-draft policy behind the run's injected draft supplier (<c>RunDirector</c>'s
/// <c>Func&lt;DraftContext, IRandomSource, Task&lt;Creature?&gt;&gt;</c>) — the acquisition-side sibling of
/// <see cref="RewardCalculator"/> / <see cref="ShopCalculator"/>: run-layer roguelite tuning, not a battle seam,
/// and <c>internal static</c> + pure so the gate is unit-testable without a DB (the creature <em>build</em> lives
/// in <see cref="EncounterFactory"/>, which owns the queries).
///
/// <para><see cref="ShouldOffer"/> is the whole gate: a themed draft is offered at most once every
/// <see cref="CadenceEveryNWins"/> wins, and then only <see cref="OfferPercent"/>% of the time, and never when
/// the fought-only pool is empty (so a run can't be offered a species it never faced, and never a dead offer).
/// The n% roll is drawn <em>only</em> on a cadence win, so a non-offer win doesn't perturb the seeded run stream.
/// All the numbers are provisional balance tuning — the tests assert the <em>rules</em> (cadence, empty-pool
/// guard, the roll boundary), not the exact percentages.</para>
/// </summary>
internal static class DraftCalculator
{
    /// <summary>A themed draft is considered at most once every this-many wins (the cadence).</summary>
    public const int CadenceEveryNWins = 3;

    /// <summary>…and then only this share of cadence wins actually offer one (the n% roll).</summary>
    public const int OfferPercent = 55;

    /// <summary>
    /// The complete offer gate: true iff this win should raise a themed-draft offer. Cadence first (no RNG), then
    /// the empty-pool guard (never a dead offer), then the n% roll (drawn only when the first two pass, so the
    /// seeded stream only moves on a cadence win with a non-empty pool).
    /// </summary>
    public static bool ShouldOffer(
        int battlesWon,
        IReadOnlyCollection<int> foughtSpecies,
        IRandomSource rng
    )
    {
        if (battlesWon <= 0 || battlesWon % CadenceEveryNWins != 0)
            return false; // not a cadence win
        if (foughtSpecies.Count == 0)
            return false; // nothing fought this biome yet → never a dead offer
        return rng.Next(100) < OfferPercent; // n% roll (only reached on a cadence win)
    }
}
