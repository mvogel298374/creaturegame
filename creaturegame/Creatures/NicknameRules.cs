namespace creaturegame.Creatures;

/// <summary>
/// Session-scoped nickname validation shared by every acquisition path (starter pick, themed draft, boss
/// catch): trim, cap length, and fall back to the creature's species-derived default name on a blank input —
/// mirroring Gen 1's own optional "Do you want to give a nickname to X?" prompt (Red/Blue, at starter pick /
/// trade / wild catch), which can be declined or cancelled out of, keeping the species name. Gen 1's actual
/// cap is 10 characters; kept here as a plain constant, not a generation seam — presentation-layer, not battle
/// math, the same call already made for party size 6 and draft cadence (docs/TODO.md — Encounter Logic Phase
/// 4). <c>docs/GENERATION_PROFILE.md</c>'s surface catalog is the natural home if a later generation's own
/// limit (Gen 6+ raised it to 12) is ever modeled.
/// </summary>
public static class NicknameRules
{
    public const int MaxLength = 10;

    /// <summary>
    /// Trims, uppercases, and caps <paramref name="raw"/> to <see cref="MaxLength"/>; a null, blank, or
    /// whitespace-only input returns <paramref name="fallback"/> (the creature's existing species-derived name)
    /// — the "declined/cancelled the nickname" case. Uppercased because Gen 1's actual nickname-entry screen
    /// offered <b>uppercase letters only</b> (no lowercase keyboard until Gen 2) — matching how every other
    /// name in this codebase is already constructed (<c>species.Name.ToUpper()</c> in
    /// <c>EncounterFactory.BuildCreature</c>).
    /// </summary>
    public static string Normalize(string? raw, string fallback)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return fallback;
        var upper = trimmed.ToUpperInvariant();
        return upper.Length > MaxLength ? upper[..MaxLength] : upper;
    }
}
