using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class TurnOrderTests
{
    [Fact]
    public void TurnPriority()
    {
        var fastCreature = new Creature("Fast") { Level = 50 };
        fastCreature.Attributes.Speed = 100;

        var slowCreature = new Creature("Slow") { Level = 50 };
        slowCreature.Attributes.Speed = 50;

        fastCreature.AddAttack(new Attack { Name = "Tackle", Accuracy = 100 });
        slowCreature.AddAttack(new Attack { Name = "Tackle", Accuracy = 100 });

        var chart = new Gen1TypeChart();
        var fastAction = new AttackAction(
            fastCreature,
            slowCreature,
            fastCreature.MoveSet[0],
            chart
        );
        var slowAction = new AttackAction(
            slowCreature,
            fastCreature,
            slowCreature.MoveSet[0],
            chart
        );

        var turnQueue = new List<IBattleAction> { slowAction, fastAction };

        var resolvedQueue = turnQueue
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.Source.Attributes.Speed)
            .ToList();

        Assert.Equal("Fast", resolvedQueue[0].Source.Name);
        Assert.Equal("Slow", resolvedQueue[1].Source.Name);
    }

    [Fact]
    public void Paralysis_EffectiveSpeedIsQuartered()
    {
        var creature = new Creature("Pikachu") { Level = 50 };
        creature.Battle.Status = StatusCondition.Paralysis;
        creature.CalculateStats();
        creature.Attributes.Speed = 100;

        int effectiveSpeed = StatusResolver.EffectiveSpeed(creature);

        Assert.Equal(25, effectiveSpeed);
    }

    [Fact]
    public void SpeedStage_Plus6_IncreasesEffectiveSpeed()
    {
        var creature = new Creature("Pikachu") { Level = 50 };
        creature.CalculateStats();
        creature.Attributes.Speed = 100;

        int baseSpeed = StatusResolver.EffectiveSpeed(creature, Gen1BattleRules.Instance);

        var stages = creature.Battle.Stages;
        stages.RaiseSpeed(6);
        creature.Battle.Stages = stages;
        int boostedSpeed = StatusResolver.EffectiveSpeed(creature, Gen1BattleRules.Instance);

        Assert.Equal(400, boostedSpeed); // 100 × 4.0
    }

    [Fact]
    public void SpeedStage_StacksWithParalysisQuartering()
    {
        // Paralysis quarters; Speed +6 gives 4×. Net = 1×, so effective speed ≈ base.
        var creature = new Creature("Pikachu") { Level = 50 };
        creature.Battle.Status = StatusCondition.Paralysis;
        creature.CalculateStats();
        creature.Attributes.Speed = 100;

        var stages = creature.Battle.Stages;
        stages.RaiseSpeed(6);
        creature.Battle.Stages = stages;

        int effective = StatusResolver.EffectiveSpeed(creature, Gen1BattleRules.Instance);

        Assert.Equal(100, effective); // 100 × 4.0 / 4 = 100
    }

    [Fact]
    public void Paralysis_EffectiveSpeedReadsItsOwnSeamMemberNotAHardcodedFour()
    {
        // The quartering divisor is IBattleRules.ParalysisSpeedDivisor, not an inline "4" — a Gen 7+
        // ruleset halves instead. Override only that member and assert EffectiveSpeed follows it.
        var creature = new Creature("Pikachu") { Level = 50 };
        creature.Battle.Status = StatusCondition.Paralysis;
        creature.CalculateStats();
        creature.Attributes.Speed = 100;

        int effectiveSpeed = StatusResolver.EffectiveSpeed(
            creature,
            new ParalysisHalvesSpeedRules()
        );

        Assert.Equal(50, effectiveSpeed); // follows the overridden divisor (2), not the Gen 1 value (4)
    }

    /// <summary>Rules double with the paralysis Speed divisor at 2 (the Gen 7+ value) instead of Gen 1's 4.</summary>
    private sealed class ParalysisHalvesSpeedRules : DelegatingBattleRules
    {
        public override int ParalysisSpeedDivisor => 2;
    }
}
