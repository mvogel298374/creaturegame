using creaturegame.Combat;

namespace creaturegame.Creatures;

public sealed class Gen1StatCalculator : IStatCalculator
{
    /// <summary>Default singleton — uses the shared global RNG for DV randomisation.</summary>
    public static readonly Gen1StatCalculator Instance = new();

    private readonly IRandomSource _rng;

    /// <param name="rng">RNG source for DV randomisation; defaults to the shared global
    /// source. Pass a <see cref="SeededRandomSource"/> for reproducible DVs.</param>
    public Gen1StatCalculator(IRandomSource? rng = null) =>
        _rng = rng ?? SystemRandomSource.Instance;

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
        creature.DvAttack = _rng.Next(16);
        creature.DvDefense = _rng.Next(16);
        creature.DvSpecial = _rng.Next(16);
        creature.DvSpeed = _rng.Next(16);
        creature.DvHP =
            ((creature.DvAttack & 1) << 3)
            | ((creature.DvDefense & 1) << 2)
            | ((creature.DvSpeed & 1) << 1)
            | (creature.DvSpecial & 1);
    }

    // Gen 1 Stat Experience: defeating a Pokémon adds its base stats to the victor's Stat Exp, per stat,
    // capped at 65535. The cap and the gain rule are Gen-1 values (Gen 3+ uses EV yields + 252/510 caps), so
    // they live here behind the seam, never inline at the battle call site. The gain only shows up in the
    // creature's actual stats on the next CalculateStats (a level-up) — this method just accumulates.
    private const int StatExpMax = 65535;

    public void AwardStatExp(Creature victor, Creature defeated)
    {
        victor.ExpHP = AddCapped(victor.ExpHP, defeated.BaseHP);
        victor.ExpAttack = AddCapped(victor.ExpAttack, defeated.BaseAttack);
        victor.ExpDefense = AddCapped(victor.ExpDefense, defeated.BaseDefense);
        victor.ExpSpecial = AddCapped(victor.ExpSpecial, defeated.BaseSpecial);
        victor.ExpSpeed = AddCapped(victor.ExpSpeed, defeated.BaseSpeed);
    }

    private static int AddCapped(int current, int gain) => Math.Min(StatExpMax, current + gain);
}
