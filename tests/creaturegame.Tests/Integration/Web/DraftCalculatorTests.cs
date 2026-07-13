using creaturegame.Combat;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The themed-draft offer policy (<see cref="DraftCalculator.ShouldOffer"/>) behind <see cref="RunDirector"/>'s
/// injected draft supplier — the acquisition-side sibling of <see cref="RewardCalculatorTests"/> /
/// <see cref="ShopCalculatorTests"/>. Pins the <em>rules</em> the DoR calls out (cadence, the never-a-dead-offer
/// empty-pool guard, and the n% roll boundary — with no RNG drawn until a cadence win with a non-empty pool),
/// not the exact provisional percentages. The creature <em>build</em> is DB-bound and lives in the factory.
/// </summary>
public class DraftCalculatorTests
{
    // A fixed IRandomSource whose Next(maxExclusive) always returns the same value — lets a test pin the roll
    // exactly on either side of the OfferPercent boundary without depending on a seed's stream position.
    private sealed class FixedRoll(int value) : IRandomSource
    {
        public int Next(int maxExclusive) => value;

        public int Next(int minInclusive, int maxExclusive) => value;

        public double NextDouble() => 0.0;
    }

    private static readonly int[] SomeFought = [1, 2, 3];

    [Fact]
    public void NonCadenceWin_NeverOffers_WithoutDrawingRng()
    {
        // A win that isn't a multiple of the cadence never offers — and must not even roll (a roll of 0 would
        // otherwise pass the n% gate, so returning false here proves the cadence check short-circuits first).
        for (int win = 1; win < DraftCalculator.CadenceEveryNWins; win++)
            Assert.False(DraftCalculator.ShouldOffer(win, SomeFought, new FixedRoll(0)));

        // Win 0 (the pre-first-win state) never offers either.
        Assert.False(DraftCalculator.ShouldOffer(0, SomeFought, new FixedRoll(0)));
    }

    [Fact]
    public void CadenceWin_WithEmptyFoughtPool_NeverOffers_NoDeadOffer()
    {
        // The fought-only guardrail: even on a cadence win with a guaranteed-pass roll, an empty pool never
        // offers — a run can never be offered a species it hasn't fought (ENCOUNTER_DESIGN.md §4).
        Assert.False(
            DraftCalculator.ShouldOffer(DraftCalculator.CadenceEveryNWins, [], new FixedRoll(0))
        );
    }

    [Fact]
    public void CadenceWin_WithPool_OffersOnlyWhenRollUnderOfferPercent()
    {
        int cadenceWin = DraftCalculator.CadenceEveryNWins;

        // Just under the threshold → offers; exactly at it → doesn't (the < boundary).
        Assert.True(
            DraftCalculator.ShouldOffer(
                cadenceWin,
                SomeFought,
                new FixedRoll(DraftCalculator.OfferPercent - 1)
            )
        );
        Assert.False(
            DraftCalculator.ShouldOffer(
                cadenceWin,
                SomeFought,
                new FixedRoll(DraftCalculator.OfferPercent)
            )
        );
    }

    [Fact]
    public void OffersFireOnEveryCadenceMultiple()
    {
        // Every multiple of the cadence is an offer opportunity (with a passing roll), so the draft recurs across
        // a long biome, not just on the first cadence hit.
        foreach (int mult in new[] { 1, 2, 3 })
            Assert.True(
                DraftCalculator.ShouldOffer(
                    DraftCalculator.CadenceEveryNWins * mult,
                    SomeFought,
                    new FixedRoll(0)
                )
            );
    }
}
