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

    private const int MaxDv = 15; // Gen 1 DVs are 4-bit: 0–15.

    // The floor of the "80–100% percentile" band — a top-band DV roll (0.8 × MaxDv = 12, so 12–15). Used by the
    // Superb quality; kept a named constant so the percentile intent is explicit rather than a bare 12.
    private const int TopPercentileFloor = 12;

    // Gen 1 DVs: four stats each drawn from the quality's band; HP DV derived from their lowest bits (ATK,
    // DEF, SPD, SPC order). Perfect is a fixed 15 (deterministic); Poor/Average roll within their range.
    public void RandomiseDvs(Creature creature, DvQuality quality)
    {
        creature.DvAttack = RollDv(quality);
        creature.DvDefense = RollDv(quality);
        creature.DvSpecial = RollDv(quality);
        creature.DvSpeed = RollDv(quality);
        creature.DvHP =
            ((creature.DvAttack & 1) << 3)
            | ((creature.DvDefense & 1) << 2)
            | ((creature.DvSpeed & 1) << 1)
            | (creature.DvSpecial & 1);
    }

    private int RollDv(DvQuality quality) =>
        quality switch
        {
            DvQuality.Perfect => MaxDv, // top-tier: fixed max, no roll
            // Superb: a coin flip per value — 50% draws from the top percentile band (12–15), else an ordinary
            // roll (0–15). Two draws on a "heads", one on a "tails", but deterministic given the seed.
            DvQuality.Superb => _rng.Next(2) == 0
                ? _rng.Next(TopPercentileFloor, MaxDv + 1) // top band: 12–15
                : _rng.Next(0, MaxDv + 1), // else the ordinary roll
            DvQuality.High => _rng.Next((MaxDv + 1) / 2, MaxDv + 1), // 8–15 (upper half)
            DvQuality.Poor => _rng.Next(0, (MaxDv + 1) / 2), // 0–7 (lower half)
            _ => _rng.Next(0, MaxDv + 1), // Average: 0–15 (the ordinary roll)
        };

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
