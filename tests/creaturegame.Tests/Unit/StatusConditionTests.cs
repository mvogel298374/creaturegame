using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class StatusConditionTests
{ // --- Status Condition Tests ---
    [Fact]
    public async Task Status_AppliedWhenMoveHasStatusEffect()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        var thunderWave = new Attack
        {
            Name = "Thunder Wave",
            BaseDamage = 0,
            Accuracy = 100,
            StatusEffect = StatusCondition.Paralysis,
            EffectChance = 100,
        };
        attacker.AddAttack(thunderWave);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Paralysis, defender.Battle.Status);
    }

    [Fact]
    public async Task Status_NotAppliedWhenTargetAlreadyHasStatus()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();
        defender.Battle.Status = StatusCondition.Burn;

        var thunderWave = new Attack
        {
            Name = "Thunder Wave",
            BaseDamage = 0,
            Accuracy = 100,
            StatusEffect = StatusCondition.Paralysis,
            EffectChance = 100,
        };
        attacker.AddAttack(thunderWave);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            emitter: ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Burn, defender.Battle.Status);
    }

    [Fact]
    public async Task Status_SleepSetsSleepTurns()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        var sleepPowder = new Attack
        {
            Name = "Sleep Powder",
            BaseDamage = 0,
            Accuracy = 100,
            StatusEffect = StatusCondition.Sleep,
            EffectChance = 100,
        };
        attacker.AddAttack(sleepPowder);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Sleep, defender.Battle.Status);
        Assert.InRange(defender.Battle.SleepTurns, 1, 7);
    }

    [Fact]
    public async Task Status_NotAppliedWhenEffectChanceFails()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        // 0% chance — should never apply
        var move = new Attack
        {
            Name = "Tackle",
            BaseDamage = 40,
            Accuracy = 100,
            StatusEffect = StatusCondition.Burn,
            EffectChance = 0,
        };
        attacker.AddAttack(move);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            emitter: ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.None, defender.Battle.Status);
    }

    // --- Status Effects in Battle Loop Tests ---

    [Fact]
    public void Sleep_SkipsActionAndDecrementsCounter()
    {
        var creature = new Creature("Drowzee") { Level = 50 };
        creature.Battle.Status = StatusCondition.Sleep;
        creature.Battle.SleepTurns = 3;
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.False(canAct);
        Assert.Equal(2, creature.Battle.SleepTurns);
        Assert.Equal(StatusCondition.Sleep, creature.Battle.Status);
    }

    [Fact]
    public void Sleep_WakesAndClearsStatusWhenCounterHitsZero()
    {
        var creature = new Creature("Drowzee") { Level = 50 };
        creature.Battle.Status = StatusCondition.Sleep;
        creature.Battle.SleepTurns = 1;
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.False(canAct);
        Assert.Equal(StatusCondition.None, creature.Battle.Status);
        Assert.Equal(0, creature.Battle.SleepTurns);
    }

    [Fact]
    public void Freeze_SkipsAction()
    {
        var creature = new Creature("Articuno") { Level = 50 };
        creature.Battle.Status = StatusCondition.Freeze;
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.False(canAct);
        Assert.Equal(StatusCondition.Freeze, creature.Battle.Status);
    }

    [Fact]
    public async Task Freeze_ThawsOnFireHitWithBurnEffect()
    {
        // Gen 1: Fire moves that can burn (e.g. Flamethrower) thaw a frozen target.
        var attacker = new Creature("Charizard") { Level = 50 };
        attacker.CalculateStats();
        attacker.AddAttack(
            new Attack
            {
                Name = "Flamethrower",
                BaseDamage = 95,
                Accuracy = 100,
                DamageType = DamageType.Fire,
                AttackType = AttackType.Special,
                StatusEffect = StatusCondition.Burn,
                EffectChance = 10,
            }
        );

        var defender = new Creature("Articuno") { Level = 50 };
        defender.Battle.Status = StatusCondition.Freeze;
        defender.CalculateStats();

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.None, defender.Battle.Status);
        Assert.True(defender.Attributes.HP < defender.Attributes.MaxHP);
    }

    [Fact]
    public async Task Freeze_FireMoveWithoutBurnEffect_DoesNotThaw()
    {
        // Gen 1: Fire Spin cannot inflict burn, so it does not thaw a frozen target.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.AddAttack(
            new Attack
            {
                Name = "Fire Spin",
                BaseDamage = 15,
                Accuracy = 70,
                DamageType = DamageType.Fire,
                AttackType = AttackType.Special,
                // No StatusEffect = Burn
            }
        );

        var defender = new Creature("Articuno") { Level = 50 };
        defender.Battle.Status = StatusCondition.Freeze;
        defender.CalculateStats();

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            emitter: ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Freeze, defender.Battle.Status);
    }

    [Fact]
    public void Burn_HalvesPhysicalAttackDamage()
    {
        // Burn damage range (31–37) is entirely below non-burn range (61–72) for these stats,
        // so the assertion holds for all random rolls.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.Battle.Status = StatusCondition.Burn;
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

        int burnedDamage = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        attacker.Battle.Status = StatusCondition.None;
        int normalDamage = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            move,
            new Gen1TypeChart()
        );

        Assert.True(
            burnedDamage < normalDamage,
            $"Burned ({burnedDamage}) should be less than normal ({normalDamage})"
        );
    }

    [Fact]
    public void Burn_EndOfTurnDamageIs1Over16MaxHP()
    {
        var creature = new Creature("Charizard") { Level = 50 };
        creature.Battle.Status = StatusCondition.Burn;
        creature.CalculateStats();
        creature.Attributes.MaxHP = 160;
        creature.Attributes.HP = 160;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        Assert.Equal(150, creature.Attributes.HP);
    }

    [Fact]
    public void Poison_EndOfTurnDamageIs1Over16MaxHP()
    {
        var creature = new Creature("Bulbasaur") { Level = 50 };
        creature.Battle.Status = StatusCondition.Poison;
        creature.CalculateStats();
        creature.Attributes.MaxHP = 160;
        creature.Attributes.HP = 160;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        Assert.Equal(150, creature.Attributes.HP);
    }

    [Fact]
    public void EndOfTurnDamage_NotAppliedToFaintedCreature()
    {
        var creature = new Creature("Bulbasaur") { Level = 50 };
        creature.Battle.Status = StatusCondition.Poison;
        creature.CalculateStats();
        creature.Attributes.HP = 0;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        Assert.Equal(0, creature.Attributes.HP);
    }

    [Fact]
    public void Confusion_SnapsOutWhenCounterReachesZero()
    {
        var creature = new Creature("Psyduck") { Level = 50 };
        creature.Battle.ConfusedTurns = 1;
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.True(canAct);
        Assert.Equal(0, creature.Battle.ConfusedTurns);
    }

    [Fact]
    public void Confusion_CounterDecrementsEachTurn()
    {
        var creature = new Creature("Psyduck") { Level = 50 };
        creature.Battle.ConfusedTurns = 3;
        creature.CalculateStats();
        creature.Attributes.HP = 9999;
        creature.Attributes.MaxHP = 9999;

        StatusResolver.CanAct(creature);

        Assert.True(
            creature.Battle.ConfusedTurns < 3,
            $"ConfusedTurns should have decremented from 3 but is {creature.Battle.ConfusedTurns}"
        );
    }

    [Fact]
    public void BadPoison_FirstTurn_Deals1_16MaxHP()
    {
        var creature = new Creature("Weezing") { Level = 50 };
        creature.Battle.Status = StatusCondition.BadPoison;
        creature.CalculateStats();
        creature.Attributes.MaxHP = 160;
        creature.Attributes.HP = 160;
        creature.Battle.ToxicCounter = 1;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        // floor(160 × 1/16) = 10
        Assert.Equal(150, creature.Attributes.HP);
        Assert.Equal(2, creature.Battle.ToxicCounter);
    }

    [Fact]
    public void BadPoison_SecondTurn_Deals2_16MaxHP()
    {
        var creature = new Creature("Weezing") { Level = 50 };
        creature.Battle.Status = StatusCondition.BadPoison;
        creature.CalculateStats();
        creature.Attributes.MaxHP = 160;
        creature.Attributes.HP = 160;
        creature.Battle.ToxicCounter = 2;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        // floor(160 × 2/16) = 20
        Assert.Equal(140, creature.Attributes.HP);
        Assert.Equal(3, creature.Battle.ToxicCounter);
    }

    [Fact]
    public void BadPoison_DoesNotBlockAction()
    {
        var creature = new Creature("Weezing") { Level = 50 };
        creature.Battle.Status = StatusCondition.BadPoison;
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.True(canAct);
    }

    [Fact]
    public void BadPoison_ResetOnNewBattle()
    {
        var creature = new Creature("Weezing") { Level = 50 };
        creature.CalculateStats();
        creature.Battle.Status = StatusCondition.BadPoison;
        creature.Battle.ToxicCounter = 7;

        creature.ResetBattleState();

        Assert.Equal(StatusCondition.None, creature.Battle.Status);
        Assert.Equal(1, creature.Battle.ToxicCounter);
    }
}
