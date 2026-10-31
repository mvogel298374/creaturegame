namespace creaturegame.Combat;

/// <summary>The gen-blind XP pipeline; every step floors. Formula and order in GENERATION_SEAMS.md → XP on faint.</summary>
public static class ExperienceFormula
{
    public static int YieldDivisor(ExperienceRules rules, int liveParticipants) =>
        rules.Split == ExperienceSplit.Yields && liveParticipants > 1 ? liveParticipants : 1;

    public static int Award(
        ExperienceRules rules,
        int baseExp,
        int enemyLevel,
        bool trainerOwned,
        int liveParticipants
    )
    {
        int xp = baseExp / YieldDivisor(rules, liveParticipants) * enemyLevel / rules.LevelDivisor;
        foreach (var boost in rules.Boosts)
        {
            if (Applies(boost, trainerOwned))
                xp += xp / 2;
        }
        return xp;
    }

    private static bool Applies(ExperienceBoost boost, bool trainerOwned) =>
        boost switch
        {
            ExperienceBoost.Trainer => trainerOwned,
            _ => throw new ArgumentOutOfRangeException(nameof(boost), boost, null),
        };
}
