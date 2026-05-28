namespace creaturegame.Creatures;

public interface IStatCalculator
{
    /// <summary>
    /// Calculates the HP stat.
    /// Gen 1: floor(((Base + DV) × 2 + floor(sqrt(StatExp)) / 4) × Level / 100) + Level + 10
    /// </summary>
    int CalculateHP(int baseStat, int dv, int statExp, int level);

    /// <summary>
    /// Calculates any non-HP stat (Attack, Defense, Special, Speed).
    /// Gen 1: floor(((Base + DV) × 2 + floor(sqrt(StatExp)) / 4) × Level / 100) + 5
    /// </summary>
    int CalculateOtherStat(int baseStat, int dv, int statExp, int level);

    /// <summary>
    /// Randomises a creature's individual values (DVs/IVs) in place.
    /// Gen 1: Attack/Defense/Special/Speed each draw from [0, 15]; HP DV derived from their low bits.
    /// Gen 3+: six independent IVs from [0, 31].
    /// </summary>
    void RandomiseDvs(Creature creature);
}
