using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Gen 1 Stat Experience (the precursor to later-gen EVs): a win adds the defeated foe's base stats to the
/// victor's Stat Exp (capped per stat), and that training is realized into actual stats only on the next
/// recalc (a level-up), never mid-level.
/// </summary>
public class StatExpGainTests
{
    private static Creature Foe(int hp, int atk, int def, int spc, int spe) =>
        new("Foe")
        {
            BaseHP = hp,
            BaseAttack = atk,
            BaseDefense = def,
            BaseSpecial = spc,
            BaseSpeed = spe,
        };

    // A level-50 creature with fixed base stats and zeroed DVs, stats already computed — so a stat change can
    // only come from Stat Exp, not a random DV roll.
    private static Creature FixedMon()
    {
        var c = new Creature("Mon")
        {
            Level = 50,
            BaseHP = 60,
            BaseAttack = 60,
            BaseDefense = 60,
            BaseSpecial = 60,
            BaseSpeed = 60,
        };
        c.DvHP = c.DvAttack = c.DvDefense = c.DvSpecial = c.DvSpeed = 0;
        c.CalculateStats();
        return c;
    }

    // ── AwardStatExp (the seam) ──────────────────────────────────────────────

    [Fact]
    public void AwardStatExp_AddsDefeatedBaseStatsPerStat()
    {
        var victor = new Creature("Victor");
        Gen1StatCalculator.Instance.AwardStatExp(victor, Foe(1, 2, 3, 4, 5));

        Assert.Equal(1, victor.ExpHP);
        Assert.Equal(2, victor.ExpAttack);
        Assert.Equal(3, victor.ExpDefense);
        Assert.Equal(4, victor.ExpSpecial);
        Assert.Equal(5, victor.ExpSpeed);
    }

    [Fact]
    public void AwardStatExp_AccumulatesAcrossWins()
    {
        var victor = new Creature("Victor");
        var foe = Foe(10, 20, 30, 40, 50);

        Gen1StatCalculator.Instance.AwardStatExp(victor, foe);
        Gen1StatCalculator.Instance.AwardStatExp(victor, foe);

        Assert.Equal(20, victor.ExpHP);
        Assert.Equal(40, victor.ExpAttack);
        Assert.Equal(100, victor.ExpSpeed);
    }

    [Fact]
    public void AwardStatExp_CapsEachStatAt65535()
    {
        var victor = new Creature("Victor") { ExpAttack = 65_500 };
        Gen1StatCalculator.Instance.AwardStatExp(victor, Foe(0, 100, 0, 0, 0));

        Assert.Equal(65_535, victor.ExpAttack); // 65500 + 100 would be 65600 → clamped
    }

    // ── Realization timing (the explicit fidelity choice) ────────────────────

    [Fact]
    public void StatExp_DoesNotChangeStatsUntilRecalculated()
    {
        var c = FixedMon();
        int attackBefore = c.Attributes.Attack;

        c.GainStatExp(Foe(0, 65_535, 0, 0, 0)); // huge Attack Stat Exp, but no recalc yet

        Assert.Equal(attackBefore, c.Attributes.Attack); // unchanged until a level-up recomputes
    }

    [Fact]
    public void StatExp_IsRealizedOnLevelUp()
    {
        var fed = FixedMon();
        var plain = FixedMon();
        fed.ExpAttack = 65_535;

        fed.LevelUp();
        plain.LevelUp();

        Assert.True(
            fed.Attributes.Attack > plain.Attributes.Attack,
            $"fed Attack {fed.Attributes.Attack} should exceed plain {plain.Attributes.Attack} after the level-up realizes Stat Exp"
        );
    }

    // ── End-to-end: the win awards it ────────────────────────────────────────

    [Fact]
    public async Task Winning_AwardsTheDefeatedFoesBaseStatsAsStatExp()
    {
        var player = TestCreatures.Make("Hero", hp: 300);
        player.AddAttack(
            new Attack("Slam", "")
            {
                Id = 1,
                BaseDamage = 200,
                Accuracy = 100,
            }
        );

        var enemy = TestCreatures.Make("Foe", hp: 20);
        enemy.BaseHP = 10;
        enemy.BaseAttack = 20;
        enemy.BaseDefense = 30;
        enemy.BaseSpecial = 40;
        enemy.BaseSpeed = 50;
        enemy.AddAttack(
            new Attack("Tackle", "")
            {
                Id = 2,
                BaseDamage = 10,
                Accuracy = 100,
            }
        );

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("Slam")
            .EnemyUses("Tackle")
            .Seed(1)
            .RunAsync();

        Assert.Equal("Hero", result.Winner);
        Assert.Equal(10, player.ExpHP);
        Assert.Equal(20, player.ExpAttack);
        Assert.Equal(30, player.ExpDefense);
        Assert.Equal(40, player.ExpSpecial);
        Assert.Equal(50, player.ExpSpeed);
    }
}
