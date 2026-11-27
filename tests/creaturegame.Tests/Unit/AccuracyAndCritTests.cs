using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class AccuracyAndCritTests
{ // --- Accuracy / Miss Tests ---
    [Fact]
    public void AZeroPercentMoveStillHitsOnARollOfZero()
    {
        // The minimum of 1 per step lifts 0 × 255 / 100 to a threshold of 1.
        Assert.Equal(1, Gen1BattleRules.Instance.GetHitThreshold(0, 0, 0));
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
                NeverHitRules.Instance,
                ConsoleBattleEventEmitter.Instance
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

    // pokered CalcHitChance, hand-run per cell: rows accuracy -6..+6, columns evasion -6..+6.
    private static readonly int[][] HitThresholdAt100 =
    [
        [252, 220, 189, 157, 126, 94, 63, 41, 31, 25, 20, 17, 15],
        [255, 248, 213, 177, 142, 106, 71, 46, 35, 28, 23, 19, 17],
        [255, 255, 252, 210, 168, 126, 84, 55, 42, 33, 27, 23, 21],
        [255, 255, 255, 255, 204, 153, 102, 67, 51, 40, 33, 28, 25],
        [255, 255, 255, 255, 254, 190, 127, 83, 63, 50, 41, 35, 31],
        [255, 255, 255, 255, 255, 252, 168, 110, 84, 67, 55, 47, 42],
        [255, 255, 255, 255, 255, 255, 255, 168, 127, 102, 84, 71, 63],
        [255, 255, 255, 255, 255, 255, 255, 252, 191, 152, 126, 106, 95],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 204, 168, 142, 127],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 254, 210, 178, 159],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 252, 214, 191],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 249, 223],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
    ];

    private static readonly int[][] HitThresholdAt85 =
    [
        [216, 189, 162, 135, 108, 81, 54, 35, 27, 21, 17, 15, 13],
        [240, 210, 180, 150, 120, 90, 60, 39, 30, 24, 19, 16, 15],
        [255, 248, 213, 177, 142, 106, 71, 46, 35, 28, 23, 19, 17],
        [255, 255, 255, 215, 172, 129, 86, 56, 43, 34, 28, 24, 21],
        [255, 255, 255, 255, 216, 162, 108, 71, 54, 43, 35, 30, 27],
        [255, 255, 255, 255, 255, 213, 142, 93, 71, 56, 46, 39, 35],
        [255, 255, 255, 255, 255, 255, 216, 142, 108, 86, 71, 60, 54],
        [255, 255, 255, 255, 255, 255, 255, 213, 162, 129, 106, 90, 81],
        [255, 255, 255, 255, 255, 255, 255, 255, 216, 172, 142, 120, 108],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 216, 178, 151, 135],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 213, 181, 162],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 249, 211, 189],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 241, 216],
    ];

    [Theory]
    [InlineData(100)]
    [InlineData(85)]
    public void HitThresholdFollowsPokeredsChainedRatiosForEveryStagePair(int accuracy)
    {
        var expected = accuracy == 100 ? HitThresholdAt100 : HitThresholdAt85;

        for (int acc = -6; acc <= 6; acc++)
        for (int eva = -6; eva <= 6; eva++)
            Assert.True(
                expected[acc + 6][eva + 6]
                    == Gen1BattleRules.Instance.GetHitThreshold(accuracy, acc, eva),
                $"{accuracy}% at accuracy {acc}, evasion {eva}"
            );
    }

    [Fact]
    public void EvasionMirrorsThroughTheStatTable_PlusOneIsTimesPoint66()
    {
        // 255 × 66/100 = 168, so 168/256 = 65.6% hits.
        Assert.Equal(168, Gen1BattleRules.Instance.GetHitThreshold(100, 0, 1));
        Assert.Equal(63, Gen1BattleRules.Instance.GetHitThreshold(100, 0, 6));
    }

    [Fact]
    public void EachHitChanceStepRaisesAZeroToOne()
    {
        // 1% is 2/255; ×25/100 floors to 0 and becomes 1, then ×25/100 again floors to 0 and becomes 1.
        Assert.Equal(1, Gen1BattleRules.Instance.GetHitThreshold(1, -6, 6));
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
    public void Gen1CritDoublesTheLevelNotTheDamage()
    {
        Assert.Equal(2, Gen1BattleRules.Instance.CritLevelMultiplier);
        Assert.Equal(1, Gen1BattleRules.Instance.CritDamageMultiplier);
        Assert.Equal(997, Gen1BattleRules.Instance.MaxBaseDamage);
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
