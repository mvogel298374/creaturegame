using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class AccuracyAndCritTests
{ // --- Accuracy / Miss Tests ---
    [Fact]
    public async Task AccuracyCheck_ZeroPercent_NeverDealtDamage()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();
        int hpBefore = defender.Attributes.HP;

        var move = new Attack
        {
            Name = "LowAcc",
            BaseDamage = 40,
            Accuracy = 0,
            AttackType = AttackType.Physical,
        };
        attacker.AddAttack(move);

        for (int i = 0; i < 20; i++)
        {
            var action = new AttackAction(
                attacker,
                defender,
                attacker.MoveSet[0],
                new Gen1TypeChart(),
                emitter: ConsoleBattleEventEmitter.Instance
            );
            await action.ExecuteAsync();
        }

        Assert.Equal(hpBefore, defender.Attributes.HP);
    }

    [Fact]
    public async Task AccuracyCheck_MissDoesNotApplyStatus()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        // 0% accuracy + guaranteed status chance — status must never land on a miss
        var move = new Attack
        {
            Name = "LowAcc",
            BaseDamage = 40,
            Accuracy = 0,
            AttackType = AttackType.Physical,
            StatusEffect = StatusCondition.Paralysis,
            EffectChance = 100,
        };
        attacker.AddAttack(move);

        for (int i = 0; i < 20; i++)
        {
            var action = new AttackAction(
                attacker,
                defender,
                attacker.MoveSet[0],
                new Gen1TypeChart(),
                emitter: ConsoleBattleEventEmitter.Instance
            );
            await action.ExecuteAsync();
        }

        Assert.Equal(StatusCondition.None, defender.Battle.Status);
    }

    [Fact]
    public void HitThreshold_100AccuracyNeutralStages_Is255()
    {
        // 100% accuracy on Gen 1 0-255 scale → threshold 255 → roll 255 always misses (1/256 bug).
        int threshold = Gen1BattleRules.Instance.GetHitThreshold(100, 0, 0);
        Assert.Equal(255, threshold);
    }

    [Fact]
    public void HitThreshold_AccuracyMinus6Stage_ReducesThreshold()
    {
        // Accuracy stage -6 → multiplier 3/9 = 0.333×; threshold reduces significantly.
        int neutral = Gen1BattleRules.Instance.GetHitThreshold(90, 0, 0);
        int negative = Gen1BattleRules.Instance.GetHitThreshold(90, -6, 0);
        Assert.True(
            negative < neutral,
            $"Negative acc stage threshold ({negative}) should be below neutral ({neutral})"
        );
    }

    [Fact]
    public void HitThreshold_EvasionPlus6Stage_ReducesThreshold()
    {
        // Defender evasion +6 → multiplier 9/3 = 3×; divides threshold, making miss more likely.
        int neutral = Gen1BattleRules.Instance.GetHitThreshold(90, 0, 0);
        int highEvade = Gen1BattleRules.Instance.GetHitThreshold(90, 0, 6);
        Assert.True(
            highEvade < neutral,
            $"High evasion threshold ({highEvade}) should be below neutral ({neutral})"
        );
    }

    [Fact]
    public void CritChance_HighCritMove_IsHigherThanNormal()
    {
        var creature = new Creature("Sandslash") { BaseSpeed = 65 };
        var normalMove = new Attack { Name = "Tackle", IsHighCrit = false };
        var highCritMove = new Attack { Name = "Slash", IsHighCrit = true };

        double normal = Gen1BattleRules.Instance.GetCritChance(creature, normalMove);
        double highCrit = Gen1BattleRules.Instance.GetCritChance(creature, highCritMove);

        Assert.True(
            highCrit > normal,
            $"High-crit chance ({highCrit:P2}) should exceed normal ({normal:P2})"
        );
    }

    [Fact]
    public void CritMultiplier_Gen1_IsTwo()
    {
        Assert.Equal(2.0, Gen1BattleRules.Instance.CritMultiplier);
    }

    [Fact]
    public void Crit_IgnoresAttackersNegativeAttackStage()
    {
        // With Gen 1 crits, a -6 Attack stage is ignored — crit uses raw Attributes.Attack.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack
        {
            Name = "Slash",
            BaseDamage = 70,
            AttackType = AttackType.Physical,
        };

        int normalCrit = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart(),
            AlwaysCritRules.Instance,
            out _
        );

        var stages = attacker.Battle.Stages;
        stages.RaiseAttack(-6);
        attacker.Battle.Stages = stages;

        int penalisedCrit = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart(),
            AlwaysCritRules.Instance,
            out _
        );

        // Gen 1: crit bypasses the -6 stage penalty → damage is unchanged.
        Assert.Equal(normalCrit, penalisedCrit);
    }

    [Fact]
    public void Crit_IgnoresDefendersPositiveDefenseStage()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack
        {
            Name = "Slash",
            BaseDamage = 70,
            AttackType = AttackType.Physical,
        };

        int normalCrit = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart(),
            AlwaysCritRules.Instance,
            out _
        );

        var stages = defender.Battle.Stages;
        stages.RaiseDefense(6);
        defender.Battle.Stages = stages;

        int boostedDefCrit = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart(),
            AlwaysCritRules.Instance,
            out _
        );

        // Gen 1: crit bypasses defender's +6 Defense boost → damage is unchanged.
        Assert.Equal(normalCrit, boostedDefCrit);
    }

    [Fact]
    public void Crit_Gen1_DropsBurnAttackPenalty()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack
        {
            Name = "Slash",
            BaseDamage = 70,
            AttackType = AttackType.Physical,
        };

        int cleanCrit = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart(),
            AlwaysCritRules.Instance,
            out _
        );

        attacker.Battle.Status = StatusCondition.Burn;
        int burnedCrit = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart(),
            AlwaysCritRules.Instance,
            out _
        );

        // Gen 1: crit ignores Burn penalty → burned and clean deal the same damage.
        Assert.Equal(cleanCrit, burnedCrit);
    }
}
