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
        
        int damage = DamageCalculator.CalculateGen1Damage(attacker, defender, move, new Gen1TypeChart());
        
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
        var action = new AttackAction(attacker, defender, move, new Gen1TypeChart());
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
        var action = new AttackAction(attacker, defender, null, new Gen1TypeChart());
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

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart());
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

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart());
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

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart());
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

        var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart());
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
            var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart());
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
            var action = new AttackAction(attacker, defender, attacker.MoveSet[0], new Gen1TypeChart());
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

        int stabDamage    = DamageCalculator.CalculateGen1Damage(attacker, defender, move, new Gen1TypeChart());

        attacker.Type1 = DamageType.Water; // no STAB on Fire move
        int nonStabDamage = DamageCalculator.CalculateGen1Damage(attacker, defender, move, new Gen1TypeChart());

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

        int stabDamage    = DamageCalculator.CalculateGen1Damage(attacker, defender, move, new Gen1TypeChart());

        attacker.Type2 = null; // remove Type2 STAB
        int nonStabDamage = DamageCalculator.CalculateGen1Damage(attacker, defender, move, new Gen1TypeChart());

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
}
