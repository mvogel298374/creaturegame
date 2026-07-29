using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Generations;
using creaturegame.Tests.TestSupport;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Integration.Web;

/// <summary>
/// The CHECK POKEMON overview snapshot: <see cref="PlayerOverviewDto.From"/> flattens the live
/// <see cref="Creature"/> into the five Gen 1 stats (with their DV + Stat-Exp) and the moveset. Verifies the
/// stat mapping (actual value / DV / Stat-Exp, single Special) and the move-row category derivation.
/// </summary>
public class PlayerOverviewDtoTests
{
    private static int _nextId = 1;

    private static Attack Move(string name, DamageType type, AttackType cls, int power) =>
        new(name, $"{name} description")
        {
            Id = _nextId++,
            DamageType = type,
            AttackType = cls,
            BaseDamage = power,
            Accuracy = 100,
            PowerPointsMax = 15,
        };

    private static Creature BuildCharizard()
    {
        var c = TestCreatures.Make(
            "CHARIZARD",
            level: 50,
            type1: DamageType.Fire,
            type2: DamageType.Flying,
            hp: 153,
            attack: 120,
            defense: 98,
            special: 130,
            speed: 121
        );
        (c.DvHP, c.DvAttack, c.DvDefense, c.DvSpecial, c.DvSpeed) = (15, 9, 12, 15, 8);
        (c.ExpHP, c.ExpAttack, c.ExpDefense, c.ExpSpecial, c.ExpSpeed) = (21440, 8200, 0, 12030, 0);
        return c;
    }

    [Fact]
    public void From_MapsTheFiveGen1Stats_WithActualValueDvAndStatExp()
    {
        var dto = PlayerOverviewDto.From(BuildCharizard(), Generation.One);

        Assert.Equal("CHARIZARD", dto.Name);
        Assert.Equal(50, dto.Level);
        Assert.Equal("Fire", dto.Type1);
        Assert.Equal("Flying", dto.Type2);
        Assert.Equal(1, dto.Generation); // Gen 1 — gates the later-gen INFO fields (all null here)
        Assert.Null(dto.Ability);
        Assert.Null(dto.Nature);

        // Exactly the five Gen 1 stats, in order — a single Special, never split.
        Assert.Equal(
            new[] { "HP", "ATK", "DEF", "SPC", "SPD" },
            dto.Stats.Select(s => s.Label).ToArray()
        );

        var hp = dto.Stats[0];
        Assert.Equal((153, 15, 21440), (hp.Value, hp.Dv, hp.StatExp));
        var spc = dto.Stats.Single(s => s.Label == "SPC");
        Assert.Equal((130, 15, 12030), (spc.Value, spc.Dv, spc.StatExp));
    }

    /// <summary>
    /// The <c>generation</c> field is <b>behaviour-bearing on the client</b> — <c>CreatureOverview.tsx</c> gates
    /// which INFO rows render on <c>d.generation &gt;= f.minGen</c> — and since Stage 1b it is projected from the
    /// run rather than a <c>const … = 1</c>. So it needs one assertion with a value that is <i>not</i> Gen 1.
    /// </summary>
    /// <remarks>
    /// Without this, every other assertion in this file passes <see cref="Generation.One"/> and expects 1, so
    /// re-hardcoding <c>1</c> inside <c>From</c> would leave the whole suite green — precisely the silent-Gen-1
    /// failure the Generation Profile feature exists to remove (<c>docs/GENERATION_PROFILE.md</c> §4.2). The cast
    /// is to an unregistered enum value on purpose: this pins the projection alone, and asserts nothing about any
    /// real generation existing.
    /// </remarks>
    [Fact]
    public void From_StampsTheRunsGeneration_NotAHardcodedGen1()
    {
        Assert.Equal(2, PlayerOverviewDto.From(BuildCharizard(), (Generation)2).Generation);
    }

    [Fact]
    public void From_DerivesMoveCategory_ByDamageAndGen1PhysicalSpecialSplit()
    {
        var c = BuildCharizard();
        c.AddAttack(Move("slash", DamageType.Normal, AttackType.Physical, 70)); // damaging, Normal → Physical
        c.AddAttack(Move("flamethrower", DamageType.Fire, AttackType.Special, 95)); // damaging, Fire → Special
        c.AddAttack(Move("growl", DamageType.Normal, AttackType.Physical, 0)); // no power → Status

        var moves = PlayerOverviewDto.From(c, Generation.One).Moves;
        string Category(string n) => moves.Single(m => m.Name == n).Category;

        Assert.Equal("Physical", Category("slash"));
        Assert.Equal("Special", Category("flamethrower"));
        Assert.Equal("Status", Category("growl"));

        var ft = moves.Single(m => m.Name == "flamethrower");
        Assert.Equal(("Fire", 95, 100, 15), (ft.Type, ft.Power, ft.Accuracy, ft.PpMax));
    }
}
