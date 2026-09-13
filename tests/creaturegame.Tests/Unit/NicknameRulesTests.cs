using creaturegame.Creatures;

namespace creaturegame.Tests.Unit;

// docs/TODO.md — Creature Naming: the shared nickname-validation rule behind every acquisition path (starter
// pick, themed draft, boss catch). The quirks to assert (DoR #6): blank/whitespace/cancelled input is a
// "declined the nickname" no-op (falls back to the species-default name, Gen 1's own decline behaviour); an
// over-length input is silently truncated to Gen 1's real 10-character cap rather than rejected; and every
// nickname is uppercased, since Gen 1's actual nickname-entry screen offered uppercase letters only (found by
// requirements-review 2026-09-14 — a mixed-case nickname is impossible in the real games).
public class NicknameRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_FallsBackToTheSpeciesName_OnANullBlankOrWhitespaceOnlyInput(string? raw)
    {
        Assert.Equal("PIKACHU", NicknameRules.Normalize(raw, "PIKACHU"));
    }

    [Fact]
    public void Normalize_TrimsSurroundingWhitespace()
    {
        Assert.Equal("SPARKY", NicknameRules.Normalize("  Sparky  ", "PIKACHU"));
    }

    [Fact]
    public void Normalize_UppercasesTheNickname_MatchingGen1sUppercaseOnlyKeyboard()
    {
        Assert.Equal("SPARKY", NicknameRules.Normalize("Sparky", "PIKACHU"));
        Assert.Equal("SPARKY", NicknameRules.Normalize("sparky", "PIKACHU"));
    }

    [Fact]
    public void Normalize_KeepsAnExactlyMaxLengthNickname()
    {
        string tenChars = "1234567890";
        Assert.Equal(10, tenChars.Length);
        Assert.Equal(tenChars, NicknameRules.Normalize(tenChars, "PIKACHU"));
    }

    [Fact]
    public void Normalize_TruncatesAnOverLengthNickname_RatherThanRejectingIt()
    {
        // Gen 1's own nickname-entry cap (Red/Blue) is 10 characters.
        Assert.Equal("1234567890", NicknameRules.Normalize("1234567890extra", "PIKACHU"));
    }
}
