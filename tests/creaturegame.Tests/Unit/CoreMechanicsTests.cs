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
        
        int damage = DamageCalculator.CalculateGen1Damage(attacker, defender, move);
        
        Assert.InRange(damage, 16, 19);
    }

    [Fact]
    public void TurnPriority()
    {
        var fastCreature = new Creature.Creature("Fast") { Level = 50 };
        fastCreature.Attributes.Speed = 100;
        
        var slowCreature = new Creature.Creature("Slow") { Level = 50 };
        slowCreature.Attributes.Speed = 50;

        var move = new Attack { Name = "Tackle", Accuracy = 100 };

        var fastAction = new AttackAction(fastCreature, slowCreature, move);
        var slowAction = new AttackAction(slowCreature, fastCreature, move);

        var turnQueue = new List<IBattleAction> { slowAction, fastAction };

        var resolvedQueue = turnQueue.OrderByDescending(a => a.Priority)
                                     .ThenByDescending(a => a.Source.Attributes.Speed)
                                     .ToList();

        Assert.Equal("Fast", resolvedQueue[0].Source.Name);
        Assert.Equal("Slow", resolvedQueue[1].Source.Name);
    }
}
