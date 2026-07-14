using creaturegame.Combat;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The boss-catch offer policy (<see cref="BossCatchCalculator.ShouldOffer"/>) behind <see cref="RunDirector"/>'s
/// injected boss-catch supplier — the boss channel's sibling of <see cref="DraftCalculatorTests"/>. Pins the one
/// <em>rule</em> the DoR calls out for this channel (the n% roll boundary), not the exact provisional percentage.
/// Unlike the draft there is no cadence or fought-pool gate: a single roll per Boss win, so this is the whole gate.
/// </summary>
public class BossCatchCalculatorTests
{
    // A fixed IRandomSource whose Next(maxExclusive) always returns the same value — pins the roll exactly on
    // either side of the CatchPercent boundary without depending on a seed's stream position.
    private sealed class FixedRoll(int value) : IRandomSource
    {
        public int Next(int maxExclusive) => value;

        public int Next(int minInclusive, int maxExclusive) => value;

        public double NextDouble() => 0.0;
    }

    [Fact]
    public void OffersOnlyWhenRollUnderCatchPercent()
    {
        // Just under the threshold → offers; exactly at it → doesn't (the < boundary).
        Assert.True(
            BossCatchCalculator.ShouldOffer(new FixedRoll(BossCatchCalculator.CatchPercent - 1))
        );
        Assert.False(
            BossCatchCalculator.ShouldOffer(new FixedRoll(BossCatchCalculator.CatchPercent))
        );
    }

    [Fact]
    public void ARollWellAboveTheThreshold_NeverOffers()
    {
        // The common case — most Boss wins don't catch, so the catch stays a rare bonus.
        Assert.False(BossCatchCalculator.ShouldOffer(new FixedRoll(99)));
    }
}
