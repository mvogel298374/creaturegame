namespace creaturegame.Creatures;

public sealed class Gen1StatCalculator : IStatCalculator
{
    public static readonly Gen1StatCalculator Instance = new();
    private Gen1StatCalculator() { }

    // Gen 1 HP: floor(((Base + DV) × 2 + floor(sqrt(StatExp)) / 4) × Level / 100) + Level + 10
    public int CalculateHP(int baseStat, int dv, int statExp, int level)
    {
        double expBonus = Math.Floor(Math.Sqrt(statExp)) / 4;
        return (int)Math.Floor(((baseStat + dv) * 2 + expBonus) * level / 100) + level + 10;
    }

    // Gen 1 other stat: same formula, + 5 instead of + Level + 10
    public int CalculateOtherStat(int baseStat, int dv, int statExp, int level)
    {
        double expBonus = Math.Floor(Math.Sqrt(statExp)) / 4;
        return (int)Math.Floor(((baseStat + dv) * 2 + expBonus) * level / 100) + 5;
    }

    // Gen 1 DVs: four stats each 0–15; HP DV derived from lowest bits (ATK, DEF, SPD, SPC order).
    public void RandomiseDvs(Creature creature)
    {
        creature.DvAttack  = Random.Shared.Next(16);
        creature.DvDefense = Random.Shared.Next(16);
        creature.DvSpecial = Random.Shared.Next(16);
        creature.DvSpeed   = Random.Shared.Next(16);
        creature.DvHP = ((creature.DvAttack  & 1) << 3) |
                        ((creature.DvDefense & 1) << 2) |
                        ((creature.DvSpeed   & 1) << 1) |
                         (creature.DvSpecial & 1);
    }
}
