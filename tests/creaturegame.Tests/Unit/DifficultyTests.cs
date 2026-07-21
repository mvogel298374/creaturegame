using creaturegame.Web.Battle;
using creaturegame.Web.Controllers;

namespace creaturegame.Tests.Unit;

// The Easy/Normal/Hard difficulty tier (docs/TODO.md — Settings Menu → Difficulty → XP bonus): the client's
// StartGameRequest.Difficulty string, GameController.ParseDifficulty's fallback, and the RunRules preset each
// difficulty resolves to (GameSessionManager.RunRulesFor). Neither call site had any test coverage before this
// (flagged by requirements-review) — a wrong dictionary key or a broken parse fallback would have shipped silent.
public class DifficultyTests
{
    [Theory]
    [InlineData(null, Difficulty.Normal)]
    [InlineData("", Difficulty.Normal)]
    [InlineData("bogus", Difficulty.Normal)]
    [InlineData("Easy", Difficulty.Easy)]
    [InlineData("easy", Difficulty.Easy)]
    [InlineData("NORMAL", Difficulty.Normal)]
    [InlineData("hard", Difficulty.Hard)]
    [InlineData("HARD", Difficulty.Hard)]
    public void ParseDifficulty_IsCaseInsensitive_AndFallsBackToNormal(
        string? input,
        Difficulty expected
    )
    {
        Assert.Equal(expected, GameController.ParseDifficulty(input));
    }

    [Fact]
    public void EveryDifficultyTier_HasAPreset_SoAFutureTierCantShipAKeyNotFoundException()
    {
        // RunRulesFor looks the tier up in an enum-keyed dictionary — a 4th tier added to the enum without a
        // matching preset would throw KeyNotFoundException at run-start with nothing catching it.
        foreach (var tier in Enum.GetValues<Difficulty>())
            GameSessionManager.RunRulesFor(tier); // must not throw
    }

    [Fact]
    public void Normal_ReproducesTheOriginalHardcodedRunTuning_SoPickingItIsATrueNoOp()
    {
        var rules = GameSessionManager.RunRulesFor(Difficulty.Normal);
        Assert.Equal(1.5, rules.XpMultiplierEarly);
        Assert.Equal(4.5, rules.XpMultiplierLate);
        Assert.Equal(0.5, rules.BenchXpShare);
    }

    [Fact]
    public void Easy_GrantsMoreXpAndBenchShareThanNormal()
    {
        var easy = GameSessionManager.RunRulesFor(Difficulty.Easy);
        var normal = GameSessionManager.RunRulesFor(Difficulty.Normal);
        Assert.True(easy.XpMultiplierEarly > normal.XpMultiplierEarly);
        Assert.True(easy.XpMultiplierLate > normal.XpMultiplierLate);
        Assert.True(easy.BenchXpShare > normal.BenchXpShare);
    }

    [Fact]
    public void Hard_GrantsLessXpAndBenchShareThanNormal_ButNeverBelowGen1Baseline()
    {
        var hard = GameSessionManager.RunRulesFor(Difficulty.Hard);
        var normal = GameSessionManager.RunRulesFor(Difficulty.Normal);
        Assert.True(hard.XpMultiplierEarly < normal.XpMultiplierEarly);
        Assert.True(hard.XpMultiplierLate < normal.XpMultiplierLate);
        Assert.True(hard.BenchXpShare < normal.BenchXpShare);

        // Never below RunRules.Default (pure Gen-1, no scaling at all) — Hard is "less generous", not punitive.
        Assert.True(hard.XpMultiplierEarly >= 1.0);
        Assert.True(hard.XpMultiplierLate >= 1.0);
    }
}
