namespace creaturegame.Combat;

/// <summary>Where a generation divides a win among its live participants. See GENERATION_SEAMS.md → XP on faint.</summary>
public enum ExperienceSplit
{
    None,
    Yields,
}

/// <summary>A conditional ×1.5 on the award, applied as x + ⌊x/2⌋ in list order.</summary>
public enum ExperienceBoost
{
    Trainer,
}

/// <summary>The per-generation terms <see cref="ExperienceFormula"/> reads. See GENERATION_SEAMS.md → XP on faint.</summary>
public sealed record ExperienceRules(
    int LevelDivisor,
    ExperienceSplit Split,
    IReadOnlyList<ExperienceBoost> Boosts
);
