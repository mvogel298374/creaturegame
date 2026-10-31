namespace creaturegame.Creatures;

/// <summary>Nickname validation for every acquisition path (PRODUCT_SPEC.md); Gen 1's 10-character cap is a plain constant.</summary>
public static class NicknameRules
{
    public const int MaxLength = 10;

    /// <summary>Trims, uppercases (Gen 1's entry screen is uppercase-only) and caps <paramref name="raw"/>; a blank input returns <paramref name="fallback"/>.</summary>
    public static string Normalize(string? raw, string fallback)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return fallback;
        var upper = trimmed.ToUpperInvariant();
        return upper.Length > MaxLength ? upper[..MaxLength] : upper;
    }
}
