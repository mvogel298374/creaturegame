using Xunit;
using creaturegame.Creature;
using creaturegame.Attacks;
using creaturegame.Combat;
using System.Collections.Generic;
using System.Linq;

namespace creaturegame.Tests;

public class CoreMechanicsTests
{
    [Fact]
    public void TestStatCalculation()
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
    public void TestDamageCalculationFormula()
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
    public void TestTurnPriority()
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
