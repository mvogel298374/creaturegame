using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class StatCalculationTests
{
    [Fact]
    public void StatCalculation()
    {
        var bulbasaur = new Creature("Tommy")
        {
            BaseHP = 45,
            BaseAttack = 49,
            BaseDefense = 49,
            BaseSpecial = 65,
            BaseSpeed = 45,
            Level = 50,
            DvAttack = 15,
            DvDefense = 15,
            DvSpecial = 15,
            DvSpeed = 15,
            DvHP = 15,
        };
        bulbasaur.CalculateStats();

        Assert.Equal(120, bulbasaur.Attributes.HP);
        Assert.Equal(69, bulbasaur.Attributes.Attack);
        Assert.Equal(85, bulbasaur.Attributes.Special);
    }

    [Fact]
    public void LevelUpStatIncrease()
    {
        var bulbasaur = new Creature("Tommy")
        {
            BaseHP = 45,
            BaseAttack = 49,
            BaseDefense = 49,
            BaseSpecial = 65,
            BaseSpeed = 45,
            Level = 5,
            DvAttack = 15,
            DvDefense = 15,
            DvSpecial = 15,
            DvSpeed = 15,
            DvHP = 15,
        };
        bulbasaur.CalculateStats();

        int oldHp = bulbasaur.Attributes.HP;
        int oldAttack = bulbasaur.Attributes.Attack;
        int oldSpecial = bulbasaur.Attributes.Special;

        bulbasaur.LevelUp();

        Assert.Equal(6, bulbasaur.Level);
        Assert.True(bulbasaur.Attributes.HP > oldHp);
        Assert.True(bulbasaur.Attributes.Attack > oldAttack);
        Assert.True(bulbasaur.Attributes.Special > oldSpecial);
    }

    // --- InitializeFromSpecies Tests ---

    [Fact]
    public void InitializeFromSpecies_SetsBaseStatsTypesAndGrowthRate()
    {
        var species = new PokemonSpecies
        {
            Id = 6,
            Name = "charizard",
            BaseHP = 78,
            BaseAttack = 84,
            BaseDefense = 78,
            BaseSpecial = 85,
            BaseSpeed = 100,
            Type1 = DamageType.Fire,
            Type2 = DamageType.Flying,
            GrowthRate = GrowthRate.MediumSlow,
        };

        var creature = new Creature("Charizard") { Level = 50 };
        creature.InitializeFromSpecies(species);

        Assert.Equal(DamageType.Fire, creature.Type1);
        Assert.Equal(DamageType.Flying, creature.Type2);
        Assert.Equal(GrowthRate.MediumSlow, creature.GrowthRate);
        // HP at level 50: floor(((78 + DvHP) * 2) * 50/100) + 60; DvHP ∈ [0,15] → [138, 153]
        Assert.InRange(creature.Attributes.HP, 138, 153);
        Assert.True(creature.Attributes.Attack > 0);
    }

    [Fact]
    public void Gen1StatCalculator_SeededRandomiseDvs_IsReproducible()
    {
        // Same seed ⇒ identical DVs, so seeded creature creation is reproducible.
        var c1 = new Creature("A");
        var c2 = new Creature("B");
        new Gen1StatCalculator(new SeededRandomSource(99)).RandomiseDvs(c1);
        new Gen1StatCalculator(new SeededRandomSource(99)).RandomiseDvs(c2);

        Assert.Equal(c1.DvAttack, c2.DvAttack);
        Assert.Equal(c1.DvDefense, c2.DvDefense);
        Assert.Equal(c1.DvSpecial, c2.DvSpecial);
        Assert.Equal(c1.DvSpeed, c2.DvSpeed);
        Assert.Equal(c1.DvHP, c2.DvHP); // HP DV is derived from the others' low bits
    }
}
