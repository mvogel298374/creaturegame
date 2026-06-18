using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.DB;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="Creature.EvolveTo"/> — the core stat/identity swap of an evolution. Pure
/// (built from in-memory <see cref="PokemonSpecies"/>, no DB). Asserts the authentic Gen 1 behaviour: the
/// evolved form's base stats are adopted and stats recomputed, current HP rises by exactly the max-HP gained
/// (damage already taken is preserved), and the individual half — DVs, Stat Exp, Level, Experience, moveset —
/// carries over untouched.
/// </summary>
public class EvolveToTests
{
    private static PokemonSpecies Bulbasaur =>
        new()
        {
            Id = 1,
            Name = "bulbasaur",
            BaseHP = 45,
            BaseAttack = 49,
            BaseDefense = 49,
            BaseSpecial = 65,
            BaseSpeed = 45,
            Type1 = DamageType.Grass,
            Type2 = DamageType.Poison,
            GrowthRate = GrowthRate.MediumSlow,
            BaseExperience = 64,
        };

    private static PokemonSpecies Ivysaur =>
        new()
        {
            Id = 2,
            Name = "ivysaur",
            BaseHP = 60,
            BaseAttack = 62,
            BaseDefense = 63,
            BaseSpecial = 80,
            BaseSpeed = 60,
            Type1 = DamageType.Grass,
            Type2 = DamageType.Poison,
            GrowthRate = GrowthRate.MediumSlow,
            BaseExperience = 142,
        };

    // A Bulbasaur at a fixed level with fixed (zeroed) individual values, so stats are deterministic.
    private static Creature BuildBulbasaur(int level)
    {
        var c = new Creature("BULBASAUR") { Level = level };
        c.DvHP = c.DvAttack = c.DvDefense = c.DvSpecial = c.DvSpeed = 0;
        c.ExpHP = c.ExpAttack = c.ExpDefense = c.ExpSpecial = c.ExpSpeed = 0;
        c.InitializeFromSpecies(Bulbasaur);
        c.Experience = c.CalculateExperienceForLevel(level);
        return c;
    }

    [Fact]
    public void EvolveTo_AdoptsNewSpeciesIdentityAndStats()
    {
        var c = BuildBulbasaur(20);
        int oldMaxHp = c.Attributes.MaxHP;

        c.EvolveTo(Ivysaur);

        Assert.Equal(2, c.SpeciesId);
        Assert.Equal("IVYSAUR", c.Name);
        Assert.Equal(60, c.BaseHP);
        Assert.Equal(80, c.BaseSpecial);
        Assert.Equal(142, c.SpeciesBaseExperience);
        Assert.Equal(DamageType.Grass, c.Type1);
        Assert.Equal(DamageType.Poison, c.Type2);
        // Higher base stats → higher computed stats.
        Assert.True(c.Attributes.MaxHP > oldMaxHp);
    }

    [Fact]
    public void EvolveTo_PreservesDamageTaken_HpRisesByMaxHpDelta()
    {
        var c = BuildBulbasaur(20);
        int oldMaxHp = c.Attributes.MaxHP;
        c.Attributes.HP = oldMaxHp - 10; // took 10 damage
        int hpBefore = c.Attributes.HP;

        c.EvolveTo(Ivysaur);

        int gained = c.Attributes.MaxHP - oldMaxHp;
        Assert.True(gained > 0);
        Assert.Equal(hpBefore + gained, c.Attributes.HP); // exactly the delta healed — damage preserved
        Assert.True(c.Attributes.HP < c.Attributes.MaxHP); // still not full (the 10 damage remains)
    }

    [Fact]
    public void EvolveTo_KeepsIndividualValuesLevelAndMoveset()
    {
        var c = BuildBulbasaur(20);
        c.DvAttack = 12;
        c.ExpSpeed = 5000;
        c.AddAttack(new Attack("vine-whip", "") { Id = 22, BaseDamage = 45 });
        int xpBefore = c.Experience;
        var movesBefore = c.MoveSet.Select(m => m.Base.Id).ToArray();

        c.EvolveTo(Ivysaur);

        Assert.Equal(20, c.Level);
        Assert.Equal(12, c.DvAttack);
        Assert.Equal(5000, c.ExpSpeed);
        Assert.Equal(xpBefore, c.Experience);
        Assert.Equal(movesBefore, c.MoveSet.Select(m => m.Base.Id).ToArray());
    }
}
