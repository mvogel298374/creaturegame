using creaturegame.Creatures;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.DB;

namespace creaturegame.Tests.Unit;

public class CoreMechanicsTests
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
        var bulbasaur = new Creature("Tommy")
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
        var fast = new Creature("Fast") { Level = 1, GrowthRate = GrowthRate.Fast };
        var medFast = new Creature("MedFast") { Level = 1, GrowthRate = GrowthRate.MediumFast };
        var medSlow = new Creature("MedSlow") { Level = 1, GrowthRate = GrowthRate.MediumSlow };
        var slow = new Creature("Slow") { Level = 1, GrowthRate = GrowthRate.Slow };

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
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats(); 
        attacker.Attributes.Attack = 100;
        
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 100;
        
        var move = new Attack { Name = "Tackle", BaseDamage = 40, AttackType = AttackType.Physical };
        
        int damage = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());
        
        Assert.InRange(damage, 16, 19);
    }

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
        var fastAction = new AttackAction(fastCreature, slowCreature, fastCreature.MoveSet[0], chart);
        var slowAction = new AttackAction(slowCreature, fastCreature, slowCreature.MoveSet[0], chart);

        var turnQueue = new List<IBattleAction> { slowAction, fastAction };

        var resolvedQueue = turnQueue.OrderByDescending(a => a.Priority)
                                     .ThenByDescending(a => a.Source.Attributes.Speed)
                                     .ToList();

        Assert.Equal("Fast", resolvedQueue[0].Source.Name);
        Assert.Equal("Slow", resolvedQueue[1].Source.Name);
    }

    // --- PP Tracking Tests ---

    [Fact]
    public async Task PP_DecrementsOnUse()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        var baseAttack = new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, PowerPointsMax = 5 };
        attacker.AddAttack(baseAttack);
        var move = attacker.MoveSet[0];

        int ppBefore = move.PowerPointsCurrent;
        var action = new AttackAction(attacker, defender, move, new Gen1TypeChart(), emitter: ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(ppBefore - 1, move.PowerPointsCurrent);
    }

    [Fact]
    public async Task PP_StruggleUsedWhenPPIsZero()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 50;
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;
        int defenderHpBefore = defender.Attributes.HP;

        var baseAttack = new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, PowerPointsMax = 1 };
        attacker.AddAttack(baseAttack);
        var move = attacker.MoveSet[0];
        move.PowerPointsCurrent = 0; // force PP exhausted

        // null signals AttackAction to use Struggle — mirrors what Battle does when IsOutOfPP
        var action = new AttackAction(attacker, defender, null, new Gen1TypeChart(), emitter: ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        // Defender should have taken damage (Struggle fired)
        Assert.True(defender.Attributes.HP < defenderHpBefore);
        // Attacker should have taken recoil
        Assert.True(attacker.Attributes.HP < attacker.Attributes.MaxHP);
        // PP should remain 0 (not decremented further)
        Assert.Equal(0, move.PowerPointsCurrent);
    }

    // --- Status Condition Tests ---

    [Fact]
    public async Task Status_AppliedWhenMoveHasStatusEffect()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        var thunderWave = new Attack { Name = "Thunder Wave", BaseDamage = 0, Accuracy = 100,
            StatusEffect = StatusCondition.Paralysis, EffectChance = 100 };
        attacker.AddAttack(thunderWave);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Paralysis, defender.Status);
    }

    [Fact]
    public async Task Status_NotAppliedWhenTargetAlreadyHasStatus()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();
        defender.Status = StatusCondition.Burn;

        var thunderWave = new Attack { Name = "Thunder Wave", BaseDamage = 0, Accuracy = 100,
            StatusEffect = StatusCondition.Paralysis, EffectChance = 100 };
        attacker.AddAttack(thunderWave);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), emitter: ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Burn, defender.Status);
    }

    [Fact]
    public async Task Status_SleepSetsSleepTurns()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        var sleepPowder = new Attack { Name = "Sleep Powder", BaseDamage = 0, Accuracy = 100,
            StatusEffect = StatusCondition.Sleep, EffectChance = 100 };
        attacker.AddAttack(sleepPowder);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Sleep, defender.Status);
        Assert.InRange(defender.SleepTurns, 1, 7);
    }

    [Fact]
    public async Task Status_NotAppliedWhenEffectChanceFails()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        // 0% chance — should never apply
        var move = new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100,
            StatusEffect = StatusCondition.Burn, EffectChance = 0 };
        attacker.AddAttack(move);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), emitter: ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.None, defender.Status);
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

    [Fact]
    public void Gen1TypeChart_IceVsFire_IsNeutral_Gen1Quirk()
    {
        // Gen 2+: Ice → Fire = 0.5x. Gen 1: 1x (quirk).
        var chart = new Gen1TypeChart();
        Assert.Equal(1.0, chart.GetMultiplier(DamageType.Ice, DamageType.Fire));
    }

    [Fact]
    public void Gen1TypeChart_BugVsPoison_Is2x_Gen1Quirk()
    {
        // Changed to 1x in Gen 2+.
        var chart = new Gen1TypeChart();
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Bug, DamageType.Poison));
    }

    [Fact]
    public void Gen1TypeChart_BugVsPsychic_Is2x_Gen1Quirk()
    {
        // Changed to 1x in Gen 2+.
        var chart = new Gen1TypeChart();
        Assert.Equal(2.0, chart.GetMultiplier(DamageType.Bug, DamageType.Psychic));
    }

    // --- Accuracy / Miss Tests ---

    [Fact]
    public async Task AccuracyCheck_ZeroPercent_NeverDealtDamage()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();
        int hpBefore = defender.Attributes.HP;

        var move = new Attack { Name = "LowAcc", BaseDamage = 40, Accuracy = 0, AttackType = AttackType.Physical };
        attacker.AddAttack(move);

        for (int i = 0; i < 20; i++)
        {
            var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), emitter: ConsoleBattleEventEmitter.Instance);
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
            Name         = "LowAcc",
            BaseDamage   = 40,
            Accuracy     = 0,
            AttackType   = AttackType.Physical,
            StatusEffect = StatusCondition.Paralysis,
            EffectChance = 100,
        };
        attacker.AddAttack(move);

        for (int i = 0; i < 20; i++)
        {
            var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), emitter: ConsoleBattleEventEmitter.Instance);
            await action.ExecuteAsync();
        }

        Assert.Equal(StatusCondition.None, defender.Status);
    }

    // --- STAB Tests ---

    [Fact]
    public void STAB_Type1Match_IncreasesDamage()
    {
        // All variables fixed; only STAB changes between two calls.
        // STAB worst case (1.5 × 217/255 ≈ 1.28) always exceeds non-STAB best case (1.0),
        // so a single sample is deterministically sufficient.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;
        attacker.Type1 = DamageType.Fire;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack { Name = "Ember", BaseDamage = 80, AttackType = AttackType.Physical, DamageType = DamageType.Fire };

        int stabDamage    = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        attacker.Type1 = DamageType.Water; // no STAB on Fire move
        int nonStabDamage = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        Assert.True(stabDamage > nonStabDamage,
            $"STAB damage ({stabDamage}) should exceed non-STAB ({nonStabDamage})");
    }

    [Fact]
    public void STAB_Type2Match_IncreasesDamage()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;
        attacker.Type1 = DamageType.Normal; // doesn't match move
        attacker.Type2 = DamageType.Fire;   // matches move → STAB via Type2

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack { Name = "Ember", BaseDamage = 80, AttackType = AttackType.Physical, DamageType = DamageType.Fire };

        int stabDamage    = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        attacker.Type2 = null; // remove Type2 STAB
        int nonStabDamage = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        Assert.True(stabDamage > nonStabDamage,
            $"Type2 STAB damage ({stabDamage}) should exceed non-STAB ({nonStabDamage})");
    }

    // --- AddAttack Constraint Tests ---

    [Fact]
    public void AddAttack_FifthMoveRejected()
    {
        var creature = new Creature("Test") { Level = 1 };
        for (int i = 1; i <= 4; i++)
            creature.AddAttack(new Attack { Id = i, Name = $"Move{i}" });

        bool result = creature.AddAttack(new Attack { Id = 5, Name = "Fifth" });

        Assert.False(result);
        Assert.Equal(4, creature.MoveSet.Count);
    }

    [Fact]
    public void AddAttack_DuplicateIdRejected()
    {
        var creature = new Creature("Test") { Level = 1 };
        creature.AddAttack(new Attack { Id = 1, Name = "Tackle" });

        bool result = creature.AddAttack(new Attack { Id = 1, Name = "AnotherTackle" });

        Assert.False(result);
        Assert.Single(creature.MoveSet);
    }

    // --- Status Effects in Battle Loop Tests ---

    [Fact]
    public void Sleep_SkipsActionAndDecrementsCounter()
    {
        var creature = new Creature("Drowzee") { Level = 50, Status = StatusCondition.Sleep, SleepTurns = 3 };
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.False(canAct);
        Assert.Equal(2, creature.SleepTurns);
        Assert.Equal(StatusCondition.Sleep, creature.Status);
    }

    [Fact]
    public void Sleep_WakesAndClearsStatusWhenCounterHitsZero()
    {
        var creature = new Creature("Drowzee") { Level = 50, Status = StatusCondition.Sleep, SleepTurns = 1 };
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.False(canAct);
        Assert.Equal(StatusCondition.None, creature.Status);
        Assert.Equal(0, creature.SleepTurns);
    }

    [Fact]
    public void Freeze_SkipsAction()
    {
        var creature = new Creature("Articuno") { Level = 50, Status = StatusCondition.Freeze };
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.False(canAct);
        Assert.Equal(StatusCondition.Freeze, creature.Status);
    }

    [Fact]
    public async Task Freeze_ThawsOnFireHitWithBurnEffect()
    {
        // Gen 1: Fire moves that can burn (e.g. Flamethrower) thaw a frozen target.
        var attacker = new Creature("Charizard") { Level = 50 };
        attacker.CalculateStats();
        attacker.AddAttack(new Attack
        {
            Name = "Flamethrower", BaseDamage = 95, Accuracy = 100,
            DamageType = DamageType.Fire, AttackType = AttackType.Special,
            StatusEffect = StatusCondition.Burn, EffectChance = 10,
        });

        var defender = new Creature("Articuno") { Level = 50, Status = StatusCondition.Freeze };
        defender.CalculateStats();

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.None, defender.Status);
        Assert.True(defender.Attributes.HP < defender.Attributes.MaxHP);
    }

    [Fact]
    public async Task Freeze_FireMoveWithoutBurnEffect_DoesNotThaw()
    {
        // Gen 1: Fire Spin cannot inflict burn, so it does not thaw a frozen target.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.AddAttack(new Attack
        {
            Name = "Fire Spin", BaseDamage = 15, Accuracy = 70,
            DamageType = DamageType.Fire, AttackType = AttackType.Special,
            // No StatusEffect = Burn
        });

        var defender = new Creature("Articuno") { Level = 50, Status = StatusCondition.Freeze };
        defender.CalculateStats();

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), emitter: ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Freeze, defender.Status);
    }

    [Fact]
    public void Paralysis_EffectiveSpeedIsQuartered()
    {
        var creature = new Creature("Pikachu") { Level = 50, Status = StatusCondition.Paralysis };
        creature.CalculateStats();
        creature.Attributes.Speed = 100;

        int effectiveSpeed = StatusResolver.EffectiveSpeed(creature);

        Assert.Equal(25, effectiveSpeed);
    }

    [Fact]
    public void Burn_HalvesPhysicalAttackDamage()
    {
        // Burn damage range (31–37) is entirely below non-burn range (61–72) for these stats,
        // so the assertion holds for all random rolls.
        var attacker = new Creature("Attacker") { Level = 50, Status = StatusCondition.Burn };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;

        var move = new Attack { Name = "Tackle", BaseDamage = 80, AttackType = AttackType.Physical };

        int burnedDamage = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        attacker.Status = StatusCondition.None;
        int normalDamage = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        Assert.True(burnedDamage < normalDamage,
            $"Burned ({burnedDamage}) should be less than normal ({normalDamage})");
    }

    [Fact]
    public void Burn_EndOfTurnDamageIs1Over16MaxHP()
    {
        var creature = new Creature("Charizard") { Level = 50, Status = StatusCondition.Burn };
        creature.CalculateStats();
        creature.Attributes.MaxHP = 160;
        creature.Attributes.HP   = 160;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        Assert.Equal(150, creature.Attributes.HP);
    }

    [Fact]
    public void Poison_EndOfTurnDamageIs1Over16MaxHP()
    {
        var creature = new Creature("Bulbasaur") { Level = 50, Status = StatusCondition.Poison };
        creature.CalculateStats();
        creature.Attributes.MaxHP = 160;
        creature.Attributes.HP   = 160;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        Assert.Equal(150, creature.Attributes.HP);
    }

    [Fact]
    public void EndOfTurnDamage_NotAppliedToFaintedCreature()
    {
        var creature = new Creature("Bulbasaur") { Level = 50, Status = StatusCondition.Poison };
        creature.CalculateStats();
        creature.Attributes.HP = 0;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        Assert.Equal(0, creature.Attributes.HP);
    }

    [Fact]
    public void Confusion_SnapsOutWhenCounterReachesZero()
    {
        var creature = new Creature("Psyduck") { Level = 50, ConfusedTurns = 1 };
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.True(canAct);
        Assert.Equal(0, creature.ConfusedTurns);
    }

    [Fact]
    public void Confusion_CounterDecrementsEachTurn()
    {
        var creature = new Creature("Psyduck") { Level = 50, ConfusedTurns = 3 };
        creature.CalculateStats();
        creature.Attributes.HP    = 9999;
        creature.Attributes.MaxHP = 9999;

        StatusResolver.CanAct(creature);

        Assert.True(creature.ConfusedTurns < 3,
            $"ConfusedTurns should have decremented from 3 but is {creature.ConfusedTurns}");
    }

    // --- Attributes Tests ---

    [Fact]
    public void Attributes_ReceiveDamage_ReducesHP()
    {
        var attrs = new Attributes { HP = 100, MaxHP = 100 };
        attrs.ReceiveDamage(30);
        Assert.Equal(70, attrs.HP);
    }

    [Fact]
    public void Attributes_ReceiveDamage_FloorsAtZero()
    {
        var attrs = new Attributes { HP = 10, MaxHP = 100 };
        attrs.ReceiveDamage(999);
        Assert.Equal(0, attrs.HP);
    }

    [Fact]
    public void Attributes_ReceiveHealing_IncreasesHP()
    {
        var attrs = new Attributes { HP = 50, MaxHP = 100 };
        attrs.ReceiveHealing(20);
        Assert.Equal(70, attrs.HP);
    }

    [Fact]
    public void Attributes_ReceiveHealing_CapsAtMaxHP()
    {
        var attrs = new Attributes { HP = 90, MaxHP = 100 };
        attrs.ReceiveHealing(50);
        Assert.Equal(100, attrs.HP);
    }

    // --- Gen1BattleRules Contract Tests ---

    [Fact]
    public void Gen1BattleRules_RollSleepTurns_IsInRange1To7()
    {
        var rules = Gen1BattleRules.Instance;
        for (int i = 0; i < 200; i++)
            Assert.InRange(rules.RollSleepTurns(), 1, 7);
    }

    [Fact]
    public void Gen1BattleRules_RollDamageVariance_IsInRange()
    {
        var rules = Gen1BattleRules.Instance;
        double min = 217.0 / 255.0;
        for (int i = 0; i < 200; i++)
            Assert.InRange(rules.RollDamageVariance(), min, 1.0);
    }

    [Fact]
    public void Gen1BattleRules_StruggleRecoil_IsHalfDamage()
    {
        var rules = Gen1BattleRules.Instance;
        var creature = new Creature("Test") { Level = 1 };
        Assert.Equal(25, rules.CalculateStruggleRecoil(creature, 50));
    }

    [Fact]
    public void Gen1BattleRules_StruggleRecoil_MinimumOne()
    {
        var rules = Gen1BattleRules.Instance;
        var creature = new Creature("Test") { Level = 1 };
        Assert.Equal(1, rules.CalculateStruggleRecoil(creature, 1));
    }

    [Fact]
    public void Gen1BattleRules_BurnAndPoisonDenominators_Are16()
    {
        var rules = Gen1BattleRules.Instance;
        Assert.Equal(16, rules.BurnDamageDenominator);
        Assert.Equal(16, rules.PoisonDamageDenominator);
    }

    // --- InitializeFromSpecies Tests ---

    [Fact]
    public void InitializeFromSpecies_SetsBaseStatsTypesAndGrowthRate()
    {
        var species = new PokemonSpecies
        {
            Id           = 6,
            Name         = "charizard",
            BaseHP       = 78,
            BaseAttack   = 84,
            BaseDefense  = 78,
            BaseSpecial  = 85,
            BaseSpeed    = 100,
            Type1        = DamageType.Fire,
            Type2        = DamageType.Flying,
            GrowthRate   = GrowthRate.MediumSlow,
        };

        var creature = new Creature("Charizard") { Level = 50 };
        creature.InitializeFromSpecies(species);

        Assert.Equal(DamageType.Fire,       creature.Type1);
        Assert.Equal(DamageType.Flying,     creature.Type2);
        Assert.Equal(GrowthRate.MediumSlow, creature.GrowthRate);
        // HP at level 50: floor(((78 + DvHP) * 2) * 50/100) + 60; DvHP ∈ [0,15] → [138, 153]
        Assert.InRange(creature.Attributes.HP, 138, 153);
        Assert.True(creature.Attributes.Attack > 0);
    }

    // ── Stat Stage Multiplier Tests ──────────────────────────────────────────

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

        var move = new Attack { Name = "Tackle", BaseDamage = 40, AttackType = AttackType.Physical };

        // Stage 0: baseline range; Stage +6: attack multiplied 4×.
        // +6 minimum (4 × 217/255 ≈ 3.4×) is always above stage-0 maximum (1×),
        // so the assertion holds for all random rolls.
        int stage0 = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        var stages = attacker.Stages;
        stages.RaiseAttack(6);
        attacker.Stages = stages;
        int stage6 = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        Assert.True(stage6 > stage0 * 2,
            $"Stage +6 ({stage6}) should be substantially higher than stage 0 ({stage0})");
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

        var move = new Attack { Name = "Tackle", BaseDamage = 80, AttackType = AttackType.Physical };

        int stage0 = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        var stages = attacker.Stages;
        stages.RaiseAttack(-6);
        attacker.Stages = stages;
        int stageM6 = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart());

        // Stage-0 minimum (217/255 ≈ 0.85×) is always above stage-(-6) maximum (0.25×).
        Assert.True(stageM6 < stage0,
            $"Stage -6 ({stageM6}) should be lower than stage 0 ({stage0})");
    }

    // ── Speed Stage + Paralysis Tests ────────────────────────────────────────

    [Fact]
    public void SpeedStage_Plus6_IncreasesEffectiveSpeed()
    {
        var creature = new Creature("Pikachu") { Level = 50 };
        creature.CalculateStats();
        creature.Attributes.Speed = 100;

        int baseSpeed = StatusResolver.EffectiveSpeed(creature, Gen1BattleRules.Instance);

        var stages = creature.Stages;
        stages.RaiseSpeed(6);
        creature.Stages = stages;
        int boostedSpeed = StatusResolver.EffectiveSpeed(creature, Gen1BattleRules.Instance);

        Assert.Equal(400, boostedSpeed); // 100 × 4.0
    }

    [Fact]
    public void SpeedStage_StacksWithParalysisQuartering()
    {
        // Paralysis quarters; Speed +6 gives 4×. Net = 1×, so effective speed ≈ base.
        var creature = new Creature("Pikachu") { Level = 50, Status = StatusCondition.Paralysis };
        creature.CalculateStats();
        creature.Attributes.Speed = 100;

        var stages = creature.Stages;
        stages.RaiseSpeed(6);
        creature.Stages = stages;

        int effective = StatusResolver.EffectiveSpeed(creature, Gen1BattleRules.Instance);

        Assert.Equal(100, effective); // 100 × 4.0 / 4 = 100
    }

    // ── Accuracy Stage / Hit Threshold Tests ─────────────────────────────────

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
        Assert.True(negative < neutral,
            $"Negative acc stage threshold ({negative}) should be below neutral ({neutral})");
    }

    [Fact]
    public void HitThreshold_EvasionPlus6Stage_ReducesThreshold()
    {
        // Defender evasion +6 → multiplier 9/3 = 3×; divides threshold, making miss more likely.
        int neutral  = Gen1BattleRules.Instance.GetHitThreshold(90, 0, 0);
        int highEvade = Gen1BattleRules.Instance.GetHitThreshold(90, 0, 6);
        Assert.True(highEvade < neutral,
            $"High evasion threshold ({highEvade}) should be below neutral ({neutral})");
    }

    // ── Critical Hit Tests ───────────────────────────────────────────────────

    [Fact]
    public void CritChance_HighCritMove_IsHigherThanNormal()
    {
        var creature = new Creature("Sandslash") { BaseSpeed = 65 };
        var normalMove   = new Attack { Name = "Tackle",    IsHighCrit = false };
        var highCritMove = new Attack { Name = "Slash",     IsHighCrit = true  };

        double normal   = Gen1BattleRules.Instance.GetCritChance(creature, normalMove);
        double highCrit = Gen1BattleRules.Instance.GetCritChance(creature, highCritMove);

        Assert.True(highCrit > normal,
            $"High-crit chance ({highCrit:P2}) should exceed normal ({normal:P2})");
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

        var move = new Attack { Name = "Slash", BaseDamage = 70, AttackType = AttackType.Physical };

        int normalCrit = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart(),
            AlwaysCritRules.Instance, out _);

        var stages = attacker.Stages;
        stages.RaiseAttack(-6);
        attacker.Stages = stages;

        int penalisedCrit = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart(),
            AlwaysCritRules.Instance, out _);

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

        var move = new Attack { Name = "Slash", BaseDamage = 70, AttackType = AttackType.Physical };

        int normalCrit = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart(),
            AlwaysCritRules.Instance, out _);

        var stages = defender.Stages;
        stages.RaiseDefense(6);
        defender.Stages = stages;

        int boostedDefCrit = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart(),
            AlwaysCritRules.Instance, out _);

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

        var move = new Attack { Name = "Slash", BaseDamage = 70, AttackType = AttackType.Physical };

        int cleanCrit = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart(),
            AlwaysCritRules.Instance, out _);

        attacker.Status = StatusCondition.Burn;
        int burnedCrit = DamageCalculator.CalculateDamage(attacker, defender, move, new Gen1TypeChart(),
            AlwaysCritRules.Instance, out _);

        // Gen 1: crit ignores Burn penalty → burned and clean deal the same damage.
        Assert.Equal(cleanCrit, burnedCrit);
    }

    // ── Stat-Stage Move Tests ────────────────────────────────────────────────

    [Fact]
    public async Task SwordsDance_RaisesAttackStageByTwo()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var move = new Attack
        {
            Id = 1, Name = "Swords Dance", BaseDamage = 0, Accuracy = 100,
            StatEffectStat   = StageStat.Attack,
            StatEffectDelta  = 2,
            StatEffectTarget = StageTarget.Self,
            StatEffectChance = 100,
        };
        attacker.AddAttack(move);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(2, attacker.Stages.Attack);
        Assert.Equal(0, defender.Stages.Attack);
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
            Id = 1, Name = "Growl", BaseDamage = 0, Accuracy = 100,
            StatEffectStat   = StageStat.Attack,
            StatEffectDelta  = -1,
            StatEffectTarget = StageTarget.Foe,
            StatEffectChance = 100,
        };
        attacker.AddAttack(move);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(-1, defender.Stages.Attack);
        Assert.Equal(0,  attacker.Stages.Attack);
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
        attacker.Stages.RaiseAttack(3);

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Stages.RaiseSpeed(-2);
        defender.Status = StatusCondition.Burn;

        var move = new Attack { Id = 1, Name = "Haze", BaseDamage = 0, Accuracy = 100, Effect = MoveEffect.Haze };
        attacker.AddAttack(move);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(0, attacker.Stages.Attack);
        Assert.Equal(0, defender.Stages.Speed);
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
            Id = 1, Name = "NeverLower", BaseDamage = 40, Accuracy = 100,
            StatEffectStat   = StageStat.Defense,
            StatEffectDelta  = -1,
            StatEffectTarget = StageTarget.Foe,
            StatEffectChance = 0,
        };
        attacker.AddAttack(move);

        for (int i = 0; i < 20; i++)
        {
            var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
            await action.ExecuteAsync();
        }

        Assert.Equal(0, defender.Stages.Defense);
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
            Id = 1, Name = "AlwaysLower", BaseDamage = 0, Accuracy = 100,
            StatEffectStat   = StageStat.Speed,
            StatEffectDelta  = -1,
            StatEffectTarget = StageTarget.Foe,
            StatEffectChance = 100,
        };
        attacker.AddAttack(move);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(-1, defender.Stages.Speed);
    }

    // ── Move Execution Completeness Tests ────────────────────────────────────

    [Fact]
    public async Task DrainMove_HealsSourceByHalfDamageDealt()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.HP = attacker.Attributes.MaxHP / 2;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var absorb = new Attack
        {
            Id = 1, Name = "Absorb", BaseDamage = 40, Accuracy = 100,
            DamageType = DamageType.Grass, AttackType = AttackType.Special,
            DamageCategory = DamageCategory.Drain, DrainPercent = 50,
        };
        attacker.AddAttack(absorb);

        int hpBefore = attacker.Attributes.HP;
        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.True(attacker.Attributes.HP > hpBefore);
    }

    [Fact]
    public async Task FixedDamage_DealsDamageIgnoringStats()
    {
        var attacker = new Creature("Attacker") { Level = 1 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 100 };
        defender.CalculateStats();

        var dragonRage = new Attack
        {
            Id = 2, Name = "DragonRage", BaseDamage = 1, Accuracy = 100,
            DamageCategory = DamageCategory.Fixed, FixedDamageValue = 40,
        };
        attacker.AddAttack(dragonRage);

        int hpBefore = defender.Attributes.HP;
        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(40, hpBefore - defender.Attributes.HP);
    }

    [Fact]
    public async Task LevelBasedDamage_DealsAttackerLevelDamage()
    {
        var attacker = new Creature("Attacker") { Level = 37 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 37 };
        defender.CalculateStats();

        var seismicToss = new Attack
        {
            Id = 3, Name = "SeismicToss", BaseDamage = 1, Accuracy = 100,
            DamageCategory = DamageCategory.LevelBased,
        };
        attacker.AddAttack(seismicToss);

        int hpBefore = defender.Attributes.HP;
        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(37, hpBefore - defender.Attributes.HP);
    }

    [Fact]
    public async Task OHKOMove_FailsIfSourceLevelLowerThanTarget()
    {
        var attacker = new Creature("Attacker") { Level = 5 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var fissure = new Attack
        {
            Id = 4, Name = "Fissure", BaseDamage = 1, Accuracy = 100,
            DamageCategory = DamageCategory.OHKO,
        };
        attacker.AddAttack(fissure);

        int hpBefore = defender.Attributes.HP;
        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(hpBefore, defender.Attributes.HP);
        Assert.True(defender.IsAlive());
    }

    [Fact]
    public async Task OHKOMove_FaintsTargetIfLevelSufficient()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 5 };
        defender.CalculateStats();

        var fissure = new Attack
        {
            Id = 4, Name = "Fissure", BaseDamage = 1, Accuracy = 100,
            DamageCategory = DamageCategory.OHKO,
        };
        attacker.AddAttack(fissure);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.False(defender.IsAlive());
    }

    [Fact]
    public async Task SelfDestruct_FaintsUser()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var explosion = new Attack
        {
            Id = 5, Name = "Explosion", BaseDamage = 250, Accuracy = 100,
            DamageType = DamageType.Normal, AttackType = AttackType.Physical,
            DamageCategory = DamageCategory.SelfDestruct,
        };
        attacker.AddAttack(explosion);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.False(attacker.IsAlive());
    }

    [Fact]
    public async Task SelfDestruct_FaintsUserEvenOnMiss()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        // Accuracy 0 → always misses under Gen1 rules (threshold = 0, any roll >= 0)
        var explosion = new Attack
        {
            Id = 5, Name = "Explosion", BaseDamage = 250, Accuracy = 0,
            DamageType = DamageType.Normal, AttackType = AttackType.Physical,
            DamageCategory = DamageCategory.SelfDestruct,
        };
        attacker.AddAttack(explosion);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), Gen1BattleRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.False(attacker.IsAlive());
    }

    [Fact]
    public async Task SuperFang_HalvesTargetCurrentHp()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.HP = 80;

        var superFang = new Attack
        {
            Id = 6, Name = "SuperFang", BaseDamage = 1, Accuracy = 100,
            DamageCategory = DamageCategory.SuperFang,
        };
        attacker.AddAttack(superFang);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.Equal(40, defender.Attributes.HP);
    }

    [Fact]
    public async Task Recharge_SourceCannotActNextTurn()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var hyperBeam = new Attack
        {
            Id = 7, Name = "HyperBeam", BaseDamage = 150, Accuracy = 100,
            DamageType = DamageType.Normal, AttackType = AttackType.Special,
            Effect = MoveEffect.Recharge,
        };
        attacker.AddAttack(hyperBeam);

        // Turn 1: use Hyper Beam → flag set
        var action1 = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action1.ExecuteAsync();
        Assert.True(attacker.IsRecharging);

        // Turn 2: restore defender HP so the recharge-blocked assertion is clean
        defender.Attributes.HP = defender.Attributes.MaxHP;
        int hpBefore = defender.Attributes.HP;

        var action2 = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action2.ExecuteAsync();

        Assert.False(attacker.IsRecharging);       // flag cleared
        Assert.Equal(hpBefore, defender.Attributes.HP); // no damage on recharge turn
    }

    [Fact]
    public async Task LeechSeed_SetsHasLeechSeedOnTarget()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var leechSeed = new Attack
        {
            Id = 8, Name = "LeechSeed", BaseDamage = 0, Accuracy = 100,
            Effect = MoveEffect.LeechSeed,
        };
        attacker.AddAttack(leechSeed);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.True(defender.HasLeechSeed);
    }

    [Fact]
    public async Task LeechSeedDrain_DrainsTargetAndHealsSource()
    {
        // Player applies Leech Seed to enemy. End-of-turn drain kills the low-HP enemy
        // and heals the player.
        var player = new Creature("Player");
        player.Attributes.MaxHP = 100;
        player.Attributes.HP    = 80;

        var enemy = new Creature("Enemy");
        enemy.Attributes.MaxHP = 16;  // drain = max(1, 16/16) = 1 per turn
        enemy.Attributes.HP    = 1;   // drain on turn 1 end kills it

        var leechSeed = new Attack { Id = 8, Name = "LeechSeed", BaseDamage = 0, Accuracy = 100, Effect = MoveEffect.LeechSeed };
        var splash    = new Attack { Id = 9, Name = "Splash",    BaseDamage = 0, Accuracy = 100 };
        player.AddAttack(leechSeed);
        enemy.AddAttack(splash);

        var battle = new Battle(player, enemy, new Gen1TypeChart(),
                                AutoSelectInput.Instance, AutoSelectInput.Instance,
                                AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.Attributes.HP > 80);
    }

    [Fact]
    public async Task Binding_SetsBindingTurnsOnTarget()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var wrap = new Attack
        {
            Id = 10, Name = "Wrap", BaseDamage = 15, Accuracy = 100,
            DamageType = DamageType.Normal, AttackType = AttackType.Physical,
            Effect = MoveEffect.Binding,
        };
        attacker.AddAttack(wrap);

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.True(defender.BindingTurnsRemaining > 0);
    }

    [Fact]
    public void Binding_BlocksTargetViaCanAct()
    {
        var creature = new Creature("Bound");
        creature.BindingTurnsRemaining = 3;

        bool canAct = StatusResolver.CanAct(creature, AlwaysHitRules.Instance);

        Assert.False(canAct);
        Assert.Equal(3, creature.BindingTurnsRemaining); // CanAct doesn't decrement; ApplyEndOfTurnDamage does
    }

    [Fact]
    public async Task TwoTurnMove_ChargesFirstThenDeliversDamage()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var fly = new Attack
        {
            Id = 11, Name = "Fly", BaseDamage = 70, Accuracy = 100,
            DamageType = DamageType.Flying, AttackType = AttackType.Physical,
            Effect = MoveEffect.TwoTurn,
        };
        attacker.AddAttack(fly);

        int hpBefore = defender.Attributes.HP;

        // Turn 1: charge phase — no damage, state set
        var action1 = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action1.ExecuteAsync();

        Assert.True(attacker.IsTwoTurnCharging);
        Assert.Equal(hpBefore, defender.Attributes.HP);

        // Turn 2: release phase — IsTwoTurnCharging was set, damage fires
        var action2 = new AttackAction(attacker, defender, attacker.ChargingMove!, new Gen1TypeChart(), AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action2.ExecuteAsync();

        Assert.False(attacker.IsTwoTurnCharging);
        Assert.True(defender.Attributes.HP < hpBefore);
    }

    [Fact]
    public async Task NeverMisses_AlwaysHitsRegardlessOfAccuracy()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        // Accuracy 0 would always miss under Gen1BattleRules; NeverMisses bypasses the check entirely
        var swift = new Attack
        {
            Id = 12, Name = "Swift", BaseDamage = 60, Accuracy = 0,
            DamageType = DamageType.Normal, AttackType = AttackType.Special,
            NeverMisses = true,
        };
        attacker.AddAttack(swift);

        int hpBefore = defender.Attributes.HP;
        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart(), Gen1BattleRules.Instance, ConsoleBattleEventEmitter.Instance);
        await action.ExecuteAsync();

        Assert.True(defender.Attributes.HP < hpBefore);
    }

    [Fact]
    public void Flinch_BlocksTargetViaCanAct_AndSelfClears()
    {
        var creature = new Creature("Flinched");
        creature.IsFlinched = true;

        bool canAct = StatusResolver.CanAct(creature, AlwaysHitRules.Instance);

        Assert.False(canAct);
        Assert.False(creature.IsFlinched); // flag self-clears after blocking
    }

    // ── Bad Poison (Toxic) Tests ─────────────────────────────────────────────

    [Fact]
    public void BadPoison_FirstTurn_Deals1_16MaxHP()
    {
        var creature = new Creature("Weezing") { Level = 50, Status = StatusCondition.BadPoison };
        creature.CalculateStats();
        creature.Attributes.MaxHP = 160;
        creature.Attributes.HP   = 160;
        creature.ToxicCounter = 1;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        // floor(160 × 1/16) = 10
        Assert.Equal(150, creature.Attributes.HP);
        Assert.Equal(2, creature.ToxicCounter);
    }

    [Fact]
    public void BadPoison_SecondTurn_Deals2_16MaxHP()
    {
        var creature = new Creature("Weezing") { Level = 50, Status = StatusCondition.BadPoison };
        creature.CalculateStats();
        creature.Attributes.MaxHP = 160;
        creature.Attributes.HP   = 160;
        creature.ToxicCounter = 2;

        StatusResolver.ApplyEndOfTurnDamage(creature);

        // floor(160 × 2/16) = 20
        Assert.Equal(140, creature.Attributes.HP);
        Assert.Equal(3, creature.ToxicCounter);
    }

    [Fact]
    public void BadPoison_DoesNotBlockAction()
    {
        var creature = new Creature("Weezing") { Level = 50, Status = StatusCondition.BadPoison };
        creature.CalculateStats();

        bool canAct = StatusResolver.CanAct(creature);

        Assert.True(canAct);
    }

    [Fact]
    public void BadPoison_ResetOnNewBattle()
    {
        var creature = new Creature("Weezing") { Level = 50 };
        creature.CalculateStats();
        creature.Status = StatusCondition.BadPoison;
        creature.ToxicCounter = 7;

        creature.ResetBattleState();

        Assert.Equal(StatusCondition.None, creature.Status);
        Assert.Equal(1, creature.ToxicCounter);
    }

    // ── XP & Levelling Tests ─────────────────────────────────────────────────

    [Fact]
    public async Task XP_AwardedToWinnerOnEnemyFaint()
    {
        // Charmander base experience = 64; at level 50, Gen 1 wild formula:
        //   floor(64 × 50 / 7) = 457 XP awarded to the winning player.
        int enemyBaseExp = 64;
        int enemyLevel   = 50;
        int expectedXP   = (int)Math.Floor((double)enemyBaseExp * enemyLevel / 7); // 457

        Console.WriteLine("--- XP Award: Enemy Faints ---");
        Console.WriteLine($"Enemy: Charmander Lv{enemyLevel}, BaseExp={enemyBaseExp}");
        Console.WriteLine($"Formula: floor({enemyBaseExp} × {enemyLevel} / 7) = {expectedXP} XP");

        var player = new Creature("Bulbasaur") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed  = 100;
        player.AddAttack(new Attack { Name = "Tackle", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = new Creature("Charmander") { Level = enemyLevel, SpeciesBaseExperience = enemyBaseExp };
        enemy.CalculateStats();
        enemy.Attributes.HP    = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(new Attack { Name = "Scratch", BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical });

        Console.WriteLine($"Player XP before battle: {player.Experience}");

        var battle = new Battle(player, enemy, new Gen1TypeChart(),
                                AutoSelectInput.Instance, AutoSelectInput.Instance,
                                AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await battle.StartFightAsync();

        Console.WriteLine($"Player XP after battle: {player.Experience} (expected {expectedXP})");

        Assert.Equal(expectedXP, player.Experience);
    }

    [Fact]
    public void XP_LevelUpTriggered_WhenThresholdReached()
    {
        // MediumFast growth rate: XP for level N = N³.
        // Level 2 threshold = 8; giving 10 XP from level 1 crosses it → levels up to 2.
        Console.WriteLine("--- XP Level-Up Threshold (MediumFast) ---");
        Console.WriteLine("Level 2 threshold = 2³ = 8 XP");

        var creature = new Creature("Bulbasaur")
        {
            Level      = 1,
            Experience = 0,
            GrowthRate = GrowthRate.MediumFast,
            BaseHP     = 45,
        };
        creature.CalculateStats();

        Console.WriteLine($"Before: Level {creature.Level}, XP {creature.Experience}");
        creature.GainExperience(10);
        Console.WriteLine($"After +10 XP: Level {creature.Level}, XP {creature.Experience}");
        Console.WriteLine("(XP accumulates; level counter steps forward past each threshold)");

        Assert.Equal(2,  creature.Level);
        Assert.Equal(10, creature.Experience);
    }

    [Fact]
    public async Task LeveledUp_EventFires()
    {
        // Player (Lv1, MediumFast) defeats an enemy with BaseExp=64 at Lv1.
        //   XP awarded = floor(64 × 1 / 7) = 9
        //   MediumFast Lv2 threshold = 8 → 9 >= 8 → one level-up
        //   MediumFast Lv3 threshold = 27 → 9 < 27 → stops at Lv2
        // Exactly one LeveledUp event should be emitted for Bulbasaur at level 2.
        int enemyBaseExp = 64;
        int enemyLevel   = 1;
        int expectedXP   = (int)Math.Floor((double)enemyBaseExp * enemyLevel / 7); // 9

        Console.WriteLine("--- LeveledUp Event Fires ---");
        Console.WriteLine($"Enemy: Rattata Lv{enemyLevel}, BaseExp={enemyBaseExp} → awards {expectedXP} XP");
        Console.WriteLine("Player starts Lv1, MediumFast — Lv2 threshold = 8 XP");
        Console.WriteLine($"{expectedXP} XP >= 8 → player reaches Lv2; {expectedXP} XP < 27 → stops there");

        var player = new Creature("Bulbasaur") { Level = 1, GrowthRate = GrowthRate.MediumFast };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed  = 100;
        player.AddAttack(new Attack { Name = "Tackle", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = new Creature("Rattata") { Level = enemyLevel, SpeciesBaseExperience = enemyBaseExp };
        enemy.CalculateStats();
        enemy.Attributes.HP    = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(new Attack { Name = "Scratch", BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical });

        var recorder = new RecordingBattleEventEmitter();
        var battle   = new Battle(player, enemy, new Gen1TypeChart(),
                                  AutoSelectInput.Instance, AutoSelectInput.Instance,
                                  AlwaysHitRules.Instance, recorder);
        await battle.StartFightAsync();

        var levelUps = recorder.Of<LeveledUp>().ToList();
        Console.WriteLine($"LeveledUp events captured: {levelUps.Count}");
        foreach (var e in levelUps)
            Console.WriteLine($"  → {e.CreatureName} grew to level {e.NewLevel}!");

        Assert.Single(levelUps);
        Assert.Equal("Bulbasaur", levelUps[0].CreatureName);
        Assert.Equal(2,                   levelUps[0].NewLevel);
    }

    [Fact]
    public async Task XP_NotAwardedToLoser()
    {
        // When the player faints, the winning enemy NPC must receive no XP.
        // XP gain is a player-only mechanic; Battle awards XP only on EnemyCreature faint.
        Console.WriteLine("--- XP Not Awarded to Losing (NPC) Side ---");
        Console.WriteLine("Player: 1 HP, Speed 1 → faints first turn");
        Console.WriteLine("Enemy wins; its Experience should remain 0 (NPCs don't gain XP)");

        var player = new Creature("Bulbasaur") { Level = 50 };
        player.CalculateStats();
        player.Attributes.HP    = 1;
        player.Attributes.MaxHP = 1;
        player.Attributes.Speed = 1;
        player.AddAttack(new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = new Creature("Charmander") { Level = 50, SpeciesBaseExperience = 64 };
        enemy.CalculateStats();
        enemy.Attributes.Attack = 999;
        enemy.Attributes.Speed  = 100;
        enemy.AddAttack(new Attack { Name = "Flamethrower", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Special });

        var battle = new Battle(player, enemy, new Gen1TypeChart(),
                                AutoSelectInput.Instance, AutoSelectInput.Instance,
                                AlwaysHitRules.Instance, ConsoleBattleEventEmitter.Instance);
        await battle.StartFightAsync();

        Console.WriteLine($"Enemy (NPC) Experience after battle: {enemy.Experience}");

        Assert.False(player.IsAlive());
        Assert.True(enemy.IsAlive());
        Assert.Equal(0, enemy.Experience);
    }

    [Fact]
    public void BuildCreature_AtLevel30_SetsCorrectStatsAndXPThreshold()
    {
        // Mirrors GameController.BuildCreature(species, allMoves, level: 30).
        // MediumFast XP threshold for Lv30 = 30³ = 27,000.
        // Lv30 Bulbasaur stats must be lower than Lv50 (attack ≤ 43 with any DVs).
        Console.WriteLine("--- Level Picker: Build Creature at Level 30 ---");
        Console.WriteLine("MediumFast XP threshold for Lv30 = 30³ = 27,000");

        var species = new PokemonSpecies
        {
            Id             = 1,
            Name           = "bulbasaur",
            BaseHP         = 45,
            BaseAttack     = 49,
            BaseDefense    = 49,
            BaseSpecial    = 65,
            BaseSpeed      = 45,
            GrowthRate     = GrowthRate.MediumFast,
            BaseExperience = 64,
        };

        int level   = 30;
        var creature = new Creature("Bulbasaur") { Level = level };
        creature.InitializeFromSpecies(species);
        creature.Experience = creature.CalculateExperienceForLevel(level);

        Console.WriteLine($"Level: {creature.Level}   XP: {creature.Experience}   " +
                          $"HP: {creature.Attributes.HP}   ATK: {creature.Attributes.Attack}   " +
                          $"BaseExp: {creature.SpeciesBaseExperience}");

        Assert.Equal(30,     creature.Level);
        Assert.Equal(27_000, creature.Experience);
        Assert.Equal(64,     creature.SpeciesBaseExperience);
        // HP at Lv30 for Bulbasaur: range [67, 76] — well below Lv50 range [128, 143]
        Assert.InRange(creature.Attributes.HP, 60, 100);
        // Attack at Lv30: range [29, 43] — below Lv50 min of 54
        Assert.InRange(creature.Attributes.Attack, 25, 50);
    }

    // ── Test helpers ─────────────────────────────────────────────────────────

    /// Captures all emitted battle events for assertion in tests. Does not write to console;
    /// pair with ConsoleBattleEventEmitter when you also want visible output.
    /// </summary>
    private sealed class RecordingBattleEventEmitter : IBattleEventEmitter
    {
        private readonly List<BattleEvent> _events = [];
        public void Emit(BattleEvent evt) => _events.Add(evt);
        public IEnumerable<T> Of<T>() where T : BattleEvent => _events.OfType<T>();
    }

    /// <summary>
    /// Deterministic battle rules for accuracy-sensitive tests: GetHitThreshold returns 256,
    /// which exceeds AccuracyRollBound (256), so Random.Next(256) &gt;= 256 is never true →
    /// moves always hit. Eliminates the Gen 1 1/256 miss flakiness in unit tests.
    /// </summary>
    private sealed class AlwaysHitRules : IBattleRules
    {
        public static readonly AlwaysHitRules Instance = new();
        private AlwaysHitRules() { }

        public bool   CanThawFrozenTarget(Attack move)                     => Gen1BattleRules.Instance.CanThawFrozenTarget(move);
        public int    FreezeRandomThawPercent                              => Gen1BattleRules.Instance.FreezeRandomThawPercent;
        public double RollDamageVariance()                                 => Gen1BattleRules.Instance.RollDamageVariance();
        public int    RollSleepTurns()                                     => Gen1BattleRules.Instance.RollSleepTurns();
        public int    CalculateStruggleRecoil(Creature s, int d)           => Gen1BattleRules.Instance.CalculateStruggleRecoil(s, d);
        public int    BurnDamageDenominator                                => Gen1BattleRules.Instance.BurnDamageDenominator;
        public int    PoisonDamageDenominator                              => Gen1BattleRules.Instance.PoisonDamageDenominator;
        public double BadPoisonDamageFraction(int toxicCounter)            => Gen1BattleRules.Instance.BadPoisonDamageFraction(toxicCounter);
        public double GetStatMultiplier(int stage)                         => Gen1BattleRules.Instance.GetStatMultiplier(stage);
        public double GetAccuracyStageMultiplier(int stage)                => Gen1BattleRules.Instance.GetAccuracyStageMultiplier(stage);
        public int    GetHitThreshold(int acc, int accStage, int evaStage) => 256; // > AccuracyRollBound → always hits
        public int    AccuracyRollBound                                    => Gen1BattleRules.Instance.AccuracyRollBound;
        public double GetCritChance(Creature a, Attack m)                  => Gen1BattleRules.Instance.GetCritChance(a, m);
        public double CritMultiplier                                       => Gen1BattleRules.Instance.CritMultiplier;
        public bool   CritIgnoresStatStages                                => Gen1BattleRules.Instance.CritIgnoresStatStages;
        public int    RollBindingTurns()                                   => Gen1BattleRules.Instance.RollBindingTurns();
        public int    BindingDamageDenominator                             => Gen1BattleRules.Instance.BindingDamageDenominator;
        public int    CalculateXpAwarded(int baseExp, int enemyLevel)      => Gen1BattleRules.Instance.CalculateXpAwarded(baseExp, enemyLevel);
    }

    /// <summary>
    /// Deterministic battle rules for crit tests: always crits, no damage variance.
    /// All other mechanics delegate to Gen1BattleRules.
    /// </summary>
    private sealed class AlwaysCritRules : IBattleRules
    {
        public static readonly AlwaysCritRules Instance = new();
        private AlwaysCritRules() { }

        public bool   CanThawFrozenTarget(Attack move)                    => Gen1BattleRules.Instance.CanThawFrozenTarget(move);
        public int    FreezeRandomThawPercent                             => Gen1BattleRules.Instance.FreezeRandomThawPercent;
        public double RollDamageVariance()                                => 1.0;
        public int    RollSleepTurns()                                    => Gen1BattleRules.Instance.RollSleepTurns();
        public int    CalculateStruggleRecoil(Creature s, int d)          => Gen1BattleRules.Instance.CalculateStruggleRecoil(s, d);
        public int    BurnDamageDenominator                               => Gen1BattleRules.Instance.BurnDamageDenominator;
        public int    PoisonDamageDenominator                             => Gen1BattleRules.Instance.PoisonDamageDenominator;
        public double BadPoisonDamageFraction(int toxicCounter)           => Gen1BattleRules.Instance.BadPoisonDamageFraction(toxicCounter);
        public double GetStatMultiplier(int stage)                        => Gen1BattleRules.Instance.GetStatMultiplier(stage);
        public double GetAccuracyStageMultiplier(int stage)               => Gen1BattleRules.Instance.GetAccuracyStageMultiplier(stage);
        public int    GetHitThreshold(int acc, int accStage, int evaStage) => Gen1BattleRules.Instance.GetHitThreshold(acc, accStage, evaStage);
        public int    AccuracyRollBound                                   => Gen1BattleRules.Instance.AccuracyRollBound;
        public double GetCritChance(Creature a, Attack m)                 => 1.0;
        public double CritMultiplier                                      => Gen1BattleRules.Instance.CritMultiplier;
        public bool   CritIgnoresStatStages                               => Gen1BattleRules.Instance.CritIgnoresStatStages;
        public int    RollBindingTurns()                                  => Gen1BattleRules.Instance.RollBindingTurns();
        public int    BindingDamageDenominator                            => Gen1BattleRules.Instance.BindingDamageDenominator;
        public int    CalculateXpAwarded(int baseExp, int enemyLevel)     => Gen1BattleRules.Instance.CalculateXpAwarded(baseExp, enemyLevel);
    }

    // ── EncounterSelector Tests ───────────────────────────────────────────────

    [Fact]
    public void EncounterSelector_Bst_SumsAllFiveStats()
    {
        var s = new PokemonSpecies { BaseHP = 45, BaseAttack = 49, BaseDefense = 49, BaseSpecial = 65, BaseSpeed = 45 };
        Assert.Equal(253, EncounterSelector.Bst(s));
    }

    [Fact]
    public void EncounterSelector_PickByBst_ReturnsSpeciesWithinFifteenPercent()
    {
        // Pool: one species at exactly playerBst, one far outside ±15%.
        int playerBst = 300;
        var pool = new List<PokemonSpecies>
        {
            Species(1, 60, 60, 60, 60, 60),  // BST 300 — inside window
            Species(2, 20, 20, 20, 20, 20),  // BST 100 — outside ±15% of 300
        };

        var result = EncounterSelector.PickByBst(pool, playerBst);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public void EncounterSelector_PickByBst_FallsBack_WhenNoCandidatesInDefaultWindow()
    {
        // Nothing within ±15% (playerBst=300, only species at BST 100 and 500).
        // Should widen until it finds something — both are within ±50% / 1.0 window.
        int playerBst = 300;
        var pool = new List<PokemonSpecies>
        {
            Species(1, 20, 20, 20, 20, 20),   // BST 100
            Species(2, 100, 100, 100, 100, 100), // BST 500
        };

        var result = EncounterSelector.PickByBst(pool, playerBst);

        Assert.NotNull(result); // fallback must return something
    }

    [Fact]
    public void EncounterSelector_PickByBst_ReturnsNull_WhenPoolIsEmpty()
    {
        var result = EncounterSelector.PickByBst([], 300);
        Assert.Null(result);
    }

    [Fact]
    public void EncounterSelector_PickByBst_NeverExceedsPoolMembers()
    {
        // Run many times; result must always come from the pool.
        var pool = new List<PokemonSpecies>
        {
            Species(1, 50, 50, 50, 50, 50),
            Species(2, 55, 55, 55, 55, 55),
            Species(3, 60, 60, 60, 60, 60),
        };
        var ids = pool.Select(s => s.Id).ToHashSet();

        for (int i = 0; i < 100; i++)
        {
            var result = EncounterSelector.PickByBst(pool, 270);
            Assert.NotNull(result);
            Assert.Contains(result.Id, ids);
        }
    }

    private static PokemonSpecies Species(int id, int hp, int atk, int def, int spc, int spd) =>
        new() { Id = id, BaseHP = hp, BaseAttack = atk, BaseDefense = def, BaseSpecial = spc, BaseSpeed = spd };
}
