namespace creaturegame.Creatures;

/// <summary>
/// Generation-specific stat formulas: how base stats + per-individual values + accumulated training turn into
/// a creature's actual stats, and how those per-individual values are rolled. Swap the implementation to
/// change generation; callers stay generation-agnostic.
///
/// <para><b>The per-individual values evolved across generations</b> — this seam is the single place that
/// difference lives, so a later generation is a new implementation, not edits to the engine:</para>
/// <list type="table">
///   <listheader><term>Concept</term><description>Gen 1–2 (DVs) vs Gen 3+ (IVs)</description></listheader>
///   <item><term>Range</term><description>DV 0–15 (4 bits) → IV 0–31 (5 bits)</description></item>
///   <item><term>Count</term><description>4 stored (Atk/Def/Spc/Spd) → 6 independent</description></item>
///   <item><term>HP</term><description>DV is <i>derived</i> from the four low bits → IV is its own independent value</description></item>
///   <item><term>Special</term><description>one shared Special DV in Gen 1 &amp; 2 (the Special <i>stat</i> splits into Sp.Atk/Sp.Def in Gen 2, but the per-individual value stays a single shared DV) → two independent IVs, Sp.Atk &amp; Sp.Def, only in Gen 3+ when DVs become IVs</description></item>
///   <item><term>Training</term><description>Stat Exp 0–65535, gain = defeated species' base stats → EVs capped 252/stat & 510 total, gain = the defeated species' EV yield</description></item>
/// </list>
/// <para>To add a generation, implement this interface (e.g. <c>Gen3StatCalculator</c>): new
/// <see cref="RandomiseDvs"/> (six 0–31 IVs incl. an independent HP IV), new <see cref="AwardStatExp"/> (EV
/// yield + caps), and the swapped stat formula. The Special split additionally touches <c>Attributes</c> and
/// <c>IBattleRules.GetOffensiveStat/GetDefensiveStat</c>. See the Multi-Generation section of <c>TODO.md</c>.</para>
/// </summary>
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

    /// <summary>
    /// Awards the per-individual <i>training</i> a victor gains for defeating <paramref name="defeated"/>,
    /// mutating the victor's accumulated values in place. This is gen-variable — the gain rule AND the cap
    /// both changed across generations — so it lives on the seam, not inline at the battle call site.
    /// <para>Gen 1 ("Stat Experience"): each stat gains the defeated species' corresponding <i>base stat</i>,
    /// capped at 65535 per stat. The gain is <b>not</b> reflected in the creature's stats until its stats are
    /// next recomputed (a level-up calls <c>CalculateStats</c>) — Gen 1 only realizes Stat Exp on a stat
    /// recalc, never mid-level. Gen 3+ would instead add the defeated species' EV yield, capped 252/stat and
    /// 510 total.</para>
    /// </summary>
    void AwardStatExp(Creature victor, Creature defeated);
}
