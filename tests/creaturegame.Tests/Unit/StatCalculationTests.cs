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
        new Gen1StatCalculator(new SeededRandomSource(99)).RandomiseDvs(c1, DvQuality.Average);
        new Gen1StatCalculator(new SeededRandomSource(99)).RandomiseDvs(c2, DvQuality.Average);

        Assert.Equal(c1.DvAttack, c2.DvAttack);
        Assert.Equal(c1.DvDefense, c2.DvDefense);
        Assert.Equal(c1.DvSpecial, c2.DvSpecial);
        Assert.Equal(c1.DvSpeed, c2.DvSpeed);
        Assert.Equal(c1.DvHP, c2.DvHP); // HP DV is derived from the others' low bits
    }

    [Fact]
    public void Gen1StatCalculator_PerfectDvs_AreMaxedAndDeterministic()
    {
        var c = new Creature("A");
        // Different seed each call, yet Perfect must always produce the same maxed DVs (no roll).
        for (int seed = 0; seed < 10; seed++)
        {
            new Gen1StatCalculator(new SeededRandomSource(seed)).RandomiseDvs(c, DvQuality.Perfect);
            Assert.Equal(15, c.DvAttack);
            Assert.Equal(15, c.DvDefense);
            Assert.Equal(15, c.DvSpecial);
            Assert.Equal(15, c.DvSpeed);
            Assert.Equal(15, c.DvHP); // all low bits set ⇒ derived HP DV is 15
        }
    }

    [Fact]
    public void Gen1StatCalculator_PoorDvs_NeverExceedSeven()
    {
        var c = new Creature("A");
        for (int seed = 0; seed < 200; seed++)
        {
            new Gen1StatCalculator(new SeededRandomSource(seed)).RandomiseDvs(c, DvQuality.Poor);
            Assert.InRange(c.DvAttack, 0, 7);
            Assert.InRange(c.DvDefense, 0, 7);
            Assert.InRange(c.DvSpecial, 0, 7);
            Assert.InRange(c.DvSpeed, 0, 7);
            // DvHP is intentionally not asserted: it's derived from the four stat DVs' low bits and so
            // ranges [0,15] regardless of quality — asserting it would test the HP derivation, not the band.
        }
    }

    [Fact]
    public void Gen1StatCalculator_HighDvs_StayInUpperHalf_ButAreNotAlwaysMax()
    {
        // High is the upper half [8,15] — strictly above Poor's ceiling, but still a roll (not fixed 15).
        var c = new Creature("A");
        bool sawBelowMax = false;
        for (int seed = 0; seed < 200; seed++)
        {
            new Gen1StatCalculator(new SeededRandomSource(seed)).RandomiseDvs(c, DvQuality.High);
            Assert.InRange(c.DvAttack, 8, 15);
            Assert.InRange(c.DvDefense, 8, 15);
            Assert.InRange(c.DvSpecial, 8, 15);
            Assert.InRange(c.DvSpeed, 8, 15);
            if (c.DvAttack < 15 || c.DvDefense < 15 || c.DvSpecial < 15 || c.DvSpeed < 15)
                sawBelowMax = true;
        }
        Assert.True(sawBelowMax); // distinct from Perfect — High actually rolls
    }

    [Fact]
    public void Gen1StatCalculator_AverageDvs_UseTheFullRange()
    {
        // Average spans 0–15: across many seeds at least one stat DV must exceed Poor's 0–7 ceiling
        // (otherwise Average would be indistinguishable from Poor).
        var c = new Creature("A");
        bool sawAboveSeven = false;
        for (int seed = 0; seed < 200; seed++)
        {
            new Gen1StatCalculator(new SeededRandomSource(seed)).RandomiseDvs(c, DvQuality.Average);
            Assert.InRange(c.DvAttack, 0, 15);
            Assert.InRange(c.DvDefense, 0, 15);
            Assert.InRange(c.DvSpecial, 0, 15);
            Assert.InRange(c.DvSpeed, 0, 15);
            if (c.DvAttack > 7 || c.DvDefense > 7 || c.DvSpecial > 7 || c.DvSpeed > 7)
                sawAboveSeven = true;
        }
        Assert.True(sawAboveSeven);
    }

    [Fact]
    public void Gen1StatCalculator_SuperbDvs_StayInRange_MostlyTopBand_ButNotAlwaysMax()
    {
        // Superb is the boss-catch band: every DV in [0,15], but each has a 50% shot at the 12–15 top band, so
        // across many seeds we must see BOTH a top-band value (≥12) and a below-top value (<12) — proving it's
        // neither Perfect (fixed 15) nor a plain roll, and that the top band is actually reached.
        var c = new Creature("A");
        bool sawTopBand = false;
        bool sawBelowTopBand = false;
        for (int seed = 0; seed < 200; seed++)
        {
            new Gen1StatCalculator(new SeededRandomSource(seed)).RandomiseDvs(c, DvQuality.Superb);
            foreach (int dv in new[] { c.DvAttack, c.DvDefense, c.DvSpecial, c.DvSpeed })
            {
                Assert.InRange(dv, 0, 15);
                if (dv >= 12)
                    sawTopBand = true;
                else
                    sawBelowTopBand = true;
            }
        }
        Assert.True(sawTopBand); // the top percentile band (12–15) is reached
        Assert.True(sawBelowTopBand); // …but not every DV — distinct from Perfect

        // And it's markedly stronger than an ordinary Average roll: over a large sample the mean Superb DV clears
        // the midpoint (7.5) comfortably, since ~half the draws are pinned to 12–15.
        double total = 0;
        int n = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            new Gen1StatCalculator(new SeededRandomSource(seed)).RandomiseDvs(c, DvQuality.Superb);
            total += c.DvAttack + c.DvDefense + c.DvSpecial + c.DvSpeed;
            n += 4;
        }
        Assert.True(total / n > 9.0, $"mean Superb DV {total / n:0.0} should exceed 9.0");
    }
}
