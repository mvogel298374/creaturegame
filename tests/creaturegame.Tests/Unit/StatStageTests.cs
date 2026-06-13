using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class StatStageTests
{
    [Fact]
    public void GetStatMultiplier_Plus6_Returns4()
    {
        Assert.Equal(4.0, Gen1BattleRules.Instance.GetStatMultiplier(6));
    }

    [Fact]
    public void GetStatMultiplier_Minus6_Returns0Point25()
    {
        Assert.Equal(0.25, Gen1BattleRules.Instance.GetStatMultiplier(-6));
    }

    [Fact]
    public void GetStatMultiplier_Zero_Returns1()
    {
        Assert.Equal(1.0, Gen1BattleRules.Instance.GetStatMultiplier(0));
    }

    [Fact]
    public void StatStage_Plus6_AttackDamageHigherThanBase()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 100;

        var move = new Attack
        {
            Name = "Tackle",
            BaseDamage = 40,
            AttackType = AttackType.Physical,
        };

        // Stage 0: baseline range; Stage +6: attack multiplied 4×.
        // +6 minimum (4 × 217/255 ≈ 3.4×) is always above stage-0 maximum (1×),
        // so the assertion holds for all random rolls.
        int stage0 = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        var stages = attacker.Battle.Stages;
        stages.RaiseAttack(6);
        attacker.Battle.Stages = stages;
        int stage6 = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        Assert.True(
            stage6 > stage0 * 2,
            $"Stage +6 ({stage6}) should be substantially higher than stage 0 ({stage0})"
        );
    }

    [Fact]
    public void StatStage_Minus6_AttackDamageLowerThanBase()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack
        {
            Name = "Tackle",
            BaseDamage = 80,
            AttackType = AttackType.Physical,
        };

        int stage0 = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        var stages = attacker.Battle.Stages;
        stages.RaiseAttack(-6);
        attacker.Battle.Stages = stages;
        int stageM6 = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        // Stage-0 minimum (217/255 ≈ 0.85×) is always above stage-(-6) maximum (0.25×).
        Assert.True(
            stageM6 < stage0,
            $"Stage -6 ({stageM6}) should be lower than stage 0 ({stage0})"
        );
    }

    [Fact]
    public async Task SwordsDance_RaisesAttackStageByTwo()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var move = new Attack
        {
            Id = 1,
            Name = "Swords Dance",
            BaseDamage = 0,
            Accuracy = 100,
            StatEffectStat = StageStat.Attack,
            StatEffectDelta = 2,
            StatEffectTarget = StageTarget.Self,
            StatEffectChance = 100,
        };
        attacker.AddAttack(move);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(2, attacker.Battle.Stages.Attack);
        Assert.Equal(0, defender.Battle.Stages.Attack);
    }

    [Fact]
    public async Task Growl_LowersEnemyAttackStage()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var move = new Attack
        {
            Id = 1,
            Name = "Growl",
            BaseDamage = 0,
            Accuracy = 100,
            StatEffectStat = StageStat.Attack,
            StatEffectDelta = -1,
            StatEffectTarget = StageTarget.Foe,
            StatEffectChance = 100,
        };
        attacker.AddAttack(move);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(-1, defender.Battle.Stages.Attack);
        Assert.Equal(0, attacker.Battle.Stages.Attack);
    }

    [Fact]
    public void StatStage_ClampedAtPlusSix()
    {
        var stages = new StatStages();
        stages.RaiseAttack(6);
        stages.RaiseAttack(2); // would be +8 without clamp
        Assert.Equal(6, stages.Attack);
    }

    [Fact]
    public void StatStage_ClampedAtMinusSix()
    {
        var stages = new StatStages();
        stages.RaiseDefense(-6);
        stages.RaiseDefense(-2); // would be -8 without clamp
        Assert.Equal(-6, stages.Defense);
    }

    [Fact]
    public async Task Haze_ClearsAllStagesOnBothCreatures()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Battle.Stages.RaiseAttack(3);

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Battle.Stages.RaiseSpeed(-2);
        defender.Battle.Status = StatusCondition.Burn;

        var move = new Attack
        {
            Id = 1,
            Name = "Haze",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.Haze,
        };
        attacker.AddAttack(move);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(0, attacker.Battle.Stages.Attack);
        Assert.Equal(0, defender.Battle.Stages.Speed);
    }

    [Fact]
    public async Task StatEffect_ZeroChance_NeverApplies()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var move = new Attack
        {
            Id = 1,
            Name = "NeverLower",
            BaseDamage = 40,
            Accuracy = 100,
            StatEffectStat = StageStat.Defense,
            StatEffectDelta = -1,
            StatEffectTarget = StageTarget.Foe,
            // Gen 1 stores one secondary chance per move; the engine reads it via
            // IBattleRules.GetSecondaryEffectChance (← EffectChance), so a 0% secondary lives here.
            EffectChance = 0,
            StatEffectChance = 0,
        };
        attacker.AddAttack(move);

        for (int i = 0; i < 20; i++)
        {
            var action = new AttackAction(
                attacker,
                defender,
                attacker.MoveSet[0],
                new Gen1TypeChart(),
                AlwaysHitRules.Instance,
                ConsoleBattleEventEmitter.Instance
            );
            await action.ExecuteAsync();
        }

        Assert.Equal(0, defender.Battle.Stages.Defense);
    }

    [Fact]
    public async Task StatEffect_HundredChance_AlwaysApplies()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var move = new Attack
        {
            Id = 1,
            Name = "AlwaysLower",
            BaseDamage = 0,
            Accuracy = 100,
            StatEffectStat = StageStat.Speed,
            StatEffectDelta = -1,
            StatEffectTarget = StageTarget.Foe,
            StatEffectChance = 100,
        };
        attacker.AddAttack(move);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(-1, defender.Battle.Stages.Speed);
    }
}
