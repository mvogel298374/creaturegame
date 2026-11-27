using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>A stat at its stage, as pokered's CalculateModifiedStat. See GENERATION_SEAMS.md §2 (Staged stats).</summary>
public static class StagedStats
{
    public static int Apply(int stat, int stage, IBattleRules rules)
    {
        var (numerator, denominator) = rules.StatStageRatio(stage);
        return Math.Clamp(stat * numerator / denominator, rules.MinStatValue, rules.MaxStatValue);
    }

    /// <summary>Whether a stat change fails because the staged stat already sits at the clamp it moves toward.</summary>
    public static bool ChangeFailsAtTheClamp(
        Creature creature,
        StageStat stat,
        int delta,
        IBattleRules rules
    )
    {
        if (
            !rules.StatChangeFailsAtTheStatClamp
            || rules.UnstagedStat(creature, stat) is not { } unmodified
        )
            return false;
        int staged = Apply(unmodified, creature.Battle.Stages.Of(stat), rules);
        return delta > 0 ? staged == rules.MaxStatValue : staged == rules.MinStatValue;
    }
}
