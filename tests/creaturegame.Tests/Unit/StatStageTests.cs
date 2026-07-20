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
    public async Task Haze_CuresTheTargetsMajorStatusButNotTheUsersOwn()
    {
        // Gen 1's HazeEffect_ only clears wEnemyMonStatus (the target's status byte) — the user's own
        // status is never touched (pokered engine/battle/move_effects/haze.asm).
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Battle.Status = StatusCondition.Paralysis;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
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

        Assert.Equal(StatusCondition.Paralysis, attacker.Battle.Status); // the user's own status survives
        Assert.Equal(StatusCondition.None, defender.Battle.Status); // the target's is cured
    }

    [Fact]
    public async Task Haze_DowngradesTheUsersOwnBadPoisonToRegularPoison()
    {
        // Gen 1's HazeEffect_ clears the "bad poison" bit for BOTH sides via CureVolatileStatuses, so a
        // badly-poisoned user keeps Poison but loses the escalating Toxic counter — distinct from curing
        // the status outright.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Battle.Status = StatusCondition.BadPoison;
        attacker.Battle.ToxicCounter = 5;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

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

        Assert.Equal(StatusCondition.Poison, attacker.Battle.Status);
        Assert.Equal(1, attacker.Battle.ToxicCounter);
    }

    [Fact]
    public void ResetForHaze_LeavesEverythingOutsideItsVerifiedClearListAlone()
    {
        // Gen 1's HazeEffect_ (pokered engine/battle/move_effects/haze.asm) touches only stat mods,
        // Confused, Disable, Mist, Focus Energy, Leech Seed, Reflect/Light Screen, and the badly-poisoned
        // bit. It never touches Substitute, Bide, Rampage, Rage, Binding/trap state, Recharge, two-turn
        // charging, Flinch, Mirror Move's last-used memory, or Counter's damage memory — a wholesale
        // `Battle = new BattleState()` would silently wipe all of these, which is the bug this pins.
        var creature = new Creature("A") { Level = 50 };
        creature.CalculateStats();
        var rampageMove = new PokemonAttack(new Attack { Id = 99, Name = "Thrash" });

        creature.Battle.SubstituteHp = 42;
        creature.Battle.BideTurnsRemaining = 2;
        creature.Battle.BideDamageAccumulated = 30;
        creature.Battle.BideMove = rampageMove;
        creature.Battle.RampageTurnsRemaining = 2;
        creature.Battle.RampageMove = rampageMove;
        creature.Battle.IsRaging = true;
        creature.Battle.RageMove = rampageMove;
        creature.Battle.BindingTurnsRemaining = 3;
        creature.Battle.IsRecharging = true;
        creature.Battle.IsTwoTurnCharging = true;
        creature.Battle.ChargingMove = rampageMove;
        creature.Battle.IsFlinched = true;
        creature.Battle.LastMoveUsed = rampageMove.Base;
        creature.Battle.LastDamageTaken = 17;
        creature.Battle.LastDamageType = DamageType.Fire;

        creature.ResetForHaze(preserveMajorStatus: true);

        Assert.Equal(42, creature.Battle.SubstituteHp);
        Assert.Equal(2, creature.Battle.BideTurnsRemaining);
        Assert.Equal(30, creature.Battle.BideDamageAccumulated);
        Assert.Same(rampageMove, creature.Battle.BideMove);
        Assert.Equal(2, creature.Battle.RampageTurnsRemaining);
        Assert.Same(rampageMove, creature.Battle.RampageMove);
        Assert.True(creature.Battle.IsRaging);
        Assert.Same(rampageMove, creature.Battle.RageMove);
        Assert.Equal(3, creature.Battle.BindingTurnsRemaining);
        Assert.True(creature.Battle.IsRecharging);
        Assert.True(creature.Battle.IsTwoTurnCharging);
        Assert.Same(rampageMove, creature.Battle.ChargingMove);
        Assert.True(creature.Battle.IsFlinched);
        Assert.Same(rampageMove.Base, creature.Battle.LastMoveUsed);
        Assert.Equal(17, creature.Battle.LastDamageTaken);
        Assert.Equal(DamageType.Fire, creature.Battle.LastDamageType);
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
