using Xunit;
using creaturegame.Creature;
using creaturegame.Attacks;
using creaturegame.Combat;
using System.Collections.Generic;
using System.Linq;

namespace creaturegame.Tests.Unit;

public class CoreMechanicsTests
{
    [Fact]
    public void StatCalculation()
    {
        var bulbasaur = new Creature.Creature("Tommy")
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
            DvHP = 15
        };
        bulbasaur.CalculateStats();

        Assert.Equal(120, bulbasaur.Attributes.HP);
        Assert.Equal(69, bulbasaur.Attributes.Attack);
        Assert.Equal(85, bulbasaur.Attributes.Special);
    }

    [Fact]
    public void LevelUpStatIncrease()
    {
        var bulbasaur = new Creature.Creature("Tommy")
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
            DvHP = 15
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

    [Fact]
    public void GainExperienceLevelUp()
    {
        var bulbasaur = new Creature.Creature("Tommy")
        {
            BaseHP = 45,
            BaseAttack = 49,
            BaseDefense = 49,
            BaseSpecial = 65,
            BaseSpeed = 45,
            Level = 1,
            Experience = 0,
            GrowthRate = GrowthRate.MediumFast
        };
        bulbasaur.CalculateStats();

        // Exp for level 2: 2^3 = 8
        bulbasaur.GainExperience(10);

        Assert.Equal(2, bulbasaur.Level);
    }

    [Fact]
    public void DifferentGrowthRatesExperience()
    {
        var fast = new Creature.Creature("Fast") { Level = 1, GrowthRate = GrowthRate.Fast };
        var medFast = new Creature.Creature("MedFast") { Level = 1, GrowthRate = GrowthRate.MediumFast };
        var medSlow = new Creature.Creature("MedSlow") { Level = 1, GrowthRate = GrowthRate.MediumSlow };
        var slow = new Creature.Creature("Slow") { Level = 1, GrowthRate = GrowthRate.Slow };

        // For level 10:
        // Fast: 0.8 * 10^3 = 800
        // MedFast: 10^3 = 1000
        // MedSlow: 1.2 * 10^3 - 15 * 10^2 + 100 * 10 - 140 = 1200 - 1500 + 1000 - 140 = 560
        // Slow: 1.25 * 10^3 = 1250

        // Give 900 exp to all
        int amount = 900;
        fast.GainExperience(amount);
        medFast.GainExperience(amount);
        medSlow.GainExperience(amount);
        slow.GainExperience(amount);

        Assert.True(fast.Level >= 10);
        Assert.True(medFast.Level < 10);
        Assert.True(medSlow.Level >= 10); // MedSlow is actually faster at low levels in Gen 1
        Assert.True(slow.Level < 10);
    }

    [Fact]
    public void DamageCalculationFormula()
    {
        var attacker = new Creature.Creature("Attacker") { Level = 50 };
        attacker.CalculateStats(); 
        attacker.Attributes.Attack = 100;
        
        var defender = new Creature.Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 100;
        
        var move = new Attack { Name = "Tackle", BaseDamage = 40, AttackType = AttackType.Physical };
        
        int damage = DamageCalculator.CalculateGen1Damage(attacker, defender, move, new Gen1TypeChart());
        
        Assert.InRange(damage, 16, 19);
    }

    [Fact]
    public void TurnPriority()
    {
        var fastCreature = new Creature.Creature("Fast") { Level = 50 };
        fastCreature.Attributes.Speed = 100;
        
        var slowCreature = new Creature.Creature("Slow") { Level = 50 };
        slowCreature.Attributes.Speed = 50;

        fastCreature.AddAttack(new Attack { Name = "Tackle", Accuracy = 100 });
        slowCreature.AddAttack(new Attack { Name = "Tackle", Accuracy = 100 });

        var chart = new Gen1TypeChart();
        var fastAction = new AttackAction(fastCreature, slowCreature, chart);
        var slowAction = new AttackAction(slowCreature, fastCreature, chart);

        var turnQueue = new List<IBattleAction> { slowAction, fastAction };

        var resolvedQueue = turnQueue.OrderByDescending(a => a.Priority)
                                     .ThenByDescending(a => a.Source.Attributes.Speed)
                                     .ToList();

        Assert.Equal("Fast", resolvedQueue[0].Source.Name);
        Assert.Equal("Slow", resolvedQueue[1].Source.Name);
    }

    // --- PP Tracking Tests ---

    [Fact]
    public void PP_DecrementsOnUse()
    {
        var attacker = new Creature.Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature.Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        var baseAttack = new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, PowerPointsMax = 5 };
        attacker.AddAttack(baseAttack);
        var move = attacker.MoveSet[0];

        int ppBefore = move.PowerPointsCurrent;
        var action = new AttackAction(attacker, defender, new Gen1TypeChart());
        action.ExecuteAsync().Wait();

        Assert.Equal(ppBefore - 1, move.PowerPointsCurrent);
    }

    [Fact]
    public void PP_StruggleUsedWhenPPIsZero()
    {
        var attacker = new Creature.Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 50;
        var defender = new Creature.Creature("Defender") { Level = 10 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;
        int defenderHpBefore = defender.Attributes.HP;

        var baseAttack = new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, PowerPointsMax = 1 };
        attacker.AddAttack(baseAttack);
        var move = attacker.MoveSet[0];
        move.PowerPointsCurrent = 0; // force PP exhausted
        attacker.Struggle = new Attack { Name = "Struggle", BaseDamage = 50, Accuracy = 100, DamageType = DamageType.Normal };

        var action = new AttackAction(attacker, defender, new Gen1TypeChart());
        action.ExecuteAsync().Wait();

        // Defender should have taken damage (Struggle fired)
        Assert.True(defender.Attributes.HP < defenderHpBefore);
        // Attacker should have taken recoil
        Assert.True(attacker.Attributes.HP < attacker.Attributes.MaxHP);
        // PP should remain 0 (not decremented further)
        Assert.Equal(0, move.PowerPointsCurrent);
    }

    // --- Type Chart Tests ---

    [Fact]
    public void Gen1TypeChart_SuperEffective_Returns2x()
    {
        var chart = new Gen1TypeChart();
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Fire, DamageType.Grass));
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Water, DamageType.Fire));
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Electric, DamageType.Water));
    }

    [Fact]
    public void Gen1TypeChart_NotVeryEffective_Returns0Point5x()
    {
        var chart = new Gen1TypeChart();
        Assert.Equal(0.5, chart.GetMultiplier(DamageType.Fire, DamageType.Water));
        Assert.Equal(0.5, chart.GetMultiplier(DamageType.Normal, DamageType.Rock));
        Assert.Equal(0.5, chart.GetMultiplier(DamageType.Grass, DamageType.Fire));
    }

    [Fact]
    public void Gen1TypeChart_Immune_Returns0x()
    {
        var chart = new Gen1TypeChart();
        Assert.Equal(0.0, chart.GetMultiplier(DamageType.Normal, DamageType.Ghost));
        Assert.Equal(0.0, chart.GetMultiplier(DamageType.Electric, DamageType.Ground));
        Assert.Equal(0.0, chart.GetMultiplier(DamageType.Ground, DamageType.Flying));
    }

    [Fact]
    public void Gen1TypeChart_GhostVsPsychic_IsImmune_Gen1Bug()
    {
        // In Gen 1 RBY, Ghost → Psychic = 0x (famous bug; should be 2x)
        var chart = new Gen1TypeChart();
        Assert.Equal(0.0, chart.GetMultiplier(DamageType.Ghost, DamageType.Psychic));
    }

    [Fact]
    public void Gen1TypeChart_PoisonVsBug_Is2x_Gen1Quirk()
    {
        // Changed to 0.5x in Gen 2+
        var chart = new Gen1TypeChart();
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Poison, DamageType.Bug));
    }

    [Fact]
    public void Gen1TypeChart_NeutralMatchup_Returns1x()
    {
        var chart = new Gen1TypeChart();
        Assert.Equal(1.0, chart.GetMultiplier(DamageType.Normal, DamageType.Normal));
        Assert.Equal(1.0, chart.GetMultiplier(DamageType.Fire, DamageType.Normal));
        Assert.Equal(1.0, chart.GetMultiplier(DamageType.Water, DamageType.Electric));
    }

    [Fact]
    public void Gen1TypeChart_DualType_MultipliesCorrectly()
    {
        // Water move vs Grass/Poison (Bulbasaur): 0.5 * 1.0 = 0.5
        var chart = new Gen1TypeChart();
        double effectiveness = DamageCalculator.GetTypeEffectiveness(
            DamageType.Water, DamageType.Grass, DamageType.Poison, chart);
        Assert.Equal(0.5, effectiveness);
    }
}
