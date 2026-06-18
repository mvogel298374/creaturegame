using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Web.Battle;

/// <summary>
/// On-demand snapshot of the live player creature for the in-battle "CHECK POKEMON" overview (INFO/STATS/MOVES
/// tabs). Read from the in-session <see cref="Creature"/> by <c>GameController</c> and rendered client-side.
/// Reflects the Gen 1 model — five stats with a single <c>Special</c>, physical/special by move type — and
/// reports the <b>permanent</b> stats (<see cref="Attributes"/>), not in-battle stage-adjusted values.
/// </summary>
public sealed record PlayerOverviewDto(
    string Name,
    int SpeciesId,
    int Level,
    string Type1,
    string? Type2,
    string Status,
    int Hp,
    int MaxHp,
    int XpThisLevel,
    int XpToNextLevel,
    int BaseStatTotal,
    int Generation,
    IReadOnlyList<StatRow> Stats,
    IReadOnlyList<MoveRow> Moves,
    // Later-generation summary fields the model doesn't carry yet — null in Gen 1. The client gates their
    // visibility on Generation (Gen 2 held item, Gen 3 ability/nature, Gen 9 Tera type), so the INFO tab's
    // layout is ready for them without showing empty rows today.
    string? HeldItem = null,
    string? Ability = null,
    string? Nature = null,
    string? TeraType = null
)
{
    /// <summary>The app's single active generation. Becomes dynamic when the multi-generation sprint lands.</summary>
    public const int ActiveGeneration = 1;

    public static PlayerOverviewDto From(Creature c)
    {
        var a = c.Attributes;
        var stats = new List<StatRow>
        {
            new("HP", a.MaxHP, c.DvHP, c.ExpHP),
            new("ATK", a.Attack, c.DvAttack, c.ExpAttack),
            new("DEF", a.Defense, c.DvDefense, c.ExpDefense),
            new("SPC", a.Special, c.DvSpecial, c.ExpSpecial),
            new("SPD", a.Speed, c.DvSpeed, c.ExpSpeed),
        };
        var moves = c.MoveSet.Select(m => MoveRow.From(m.Base, m.PowerPointsCurrent)).ToList();
        return new PlayerOverviewDto(
            c.Name,
            c.SpeciesId,
            c.Level,
            (c.Type1 ?? DamageType.Normal).ToString(),
            c.Type2?.ToString(),
            c.Battle.Status.ToString(),
            a.HP,
            a.MaxHP,
            c.XpThisLevel,
            c.XpToNextLevel,
            c.BaseHP + c.BaseAttack + c.BaseDefense + c.BaseSpecial + c.BaseSpeed,
            ActiveGeneration,
            stats,
            moves
        );
    }
}

/// <summary>One Gen 1 stat (HP/ATK/DEF/SPC/SPD): the actual computed value plus its DV (0–15) and Stat-Exp.</summary>
public sealed record StatRow(string Label, int Value, int Dv, int StatExp);

/// <summary>One moveset entry, flattened for display.</summary>
public sealed record MoveRow(
    string Name,
    string Type,
    string Category,
    int Power,
    int Accuracy,
    int PpCurrent,
    int PpMax,
    string? Description
)
{
    public static MoveRow From(Attack a, int ppCurrent)
    {
        // Same "is this move damaging" test as LearnsetMoveSelector: positive base power, or a non-standard
        // damage formula (fixed-damage, level-based, OHKO, etc.). Damaging moves show their Gen 1 physical/
        // special class (by type); everything else is a Status move.
        bool damaging = a.BaseDamage > 0 || a.DamageCategory != DamageCategory.Standard;
        return new MoveRow(
            a.Name ?? "",
            a.DamageType.ToString(),
            damaging ? a.AttackType.ToString() : "Status",
            a.BaseDamage,
            a.Accuracy,
            ppCurrent,
            a.PowerPointsMax,
            a.Description
        );
    }
}
