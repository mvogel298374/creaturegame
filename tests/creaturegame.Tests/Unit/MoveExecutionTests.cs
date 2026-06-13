using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class MoveExecutionTests
{
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
            Id = 1,
            Name = "Absorb",
            BaseDamage = 40,
            Accuracy = 100,
            DamageType = DamageType.Grass,
            AttackType = AttackType.Special,
            DamageCategory = DamageCategory.Drain,
            DrainPercent = 50,
        };
        attacker.AddAttack(absorb);

        int hpBefore = attacker.Attributes.HP;
        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
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
            Id = 2,
            Name = "DragonRage",
            BaseDamage = 1,
            Accuracy = 100,
            DamageCategory = DamageCategory.Fixed,
            FixedDamageValue = 40,
        };
        attacker.AddAttack(dragonRage);

        int hpBefore = defender.Attributes.HP;
        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
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
            Id = 3,
            Name = "SeismicToss",
            BaseDamage = 1,
            Accuracy = 100,
            DamageCategory = DamageCategory.LevelBased,
        };
        attacker.AddAttack(seismicToss);

        int hpBefore = defender.Attributes.HP;
        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(37, hpBefore - defender.Attributes.HP);
    }

    [Fact]
    public async Task OHKOMove_FailsIfTargetFasterThanSource()
    {
        // Gen 1 OHKO is a SPEED comparison (not the level check Gen 2 added). Set Speed explicitly so
        // the outcome doesn't ride on randomised DVs flipping the order (the old level-based setup was
        // flaky). Here the target out-speeds the user, so Fissure fails outright.
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Speed = 50;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Speed = 100;

        var fissure = new Attack
        {
            Id = 4,
            Name = "Fissure",
            BaseDamage = 1,
            Accuracy = 100,
            DamageCategory = DamageCategory.OHKO,
        };
        attacker.AddAttack(fissure);

        int hpBefore = defender.Attributes.HP;
        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(hpBefore, defender.Attributes.HP);
        Assert.True(defender.IsAlive());
    }

    [Fact]
    public async Task OHKOMove_FaintsTargetIfSourceAtLeastAsFast()
    {
        // Gen 1 OHKO succeeds when the user is at least as fast as the target — a SPEED comparison, not
        // the level check Gen 2 added. Set Speed explicitly so the result is deterministic (the old
        // level-based setup relied on DVs and was flaky).
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        attacker.Attributes.Speed = 100;

        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        defender.Attributes.Speed = 50;

        var fissure = new Attack
        {
            Id = 4,
            Name = "Fissure",
            BaseDamage = 1,
            Accuracy = 100,
            DamageCategory = DamageCategory.OHKO,
        };
        attacker.AddAttack(fissure);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
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
            Id = 5,
            Name = "Explosion",
            BaseDamage = 250,
            Accuracy = 100,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Physical,
            DamageCategory = DamageCategory.SelfDestruct,
        };
        attacker.AddAttack(explosion);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
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
            Id = 5,
            Name = "Explosion",
            BaseDamage = 250,
            Accuracy = 0,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Physical,
            DamageCategory = DamageCategory.SelfDestruct,
        };
        attacker.AddAttack(explosion);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            Gen1BattleRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.False(attacker.IsAlive());
    }

    [Fact]
    public async Task SelfDestruct_HalvesTargetDefenseForExtraDamage()
    {
        // Gen 1 quirk: Self-Destruct/Explosion halve the target's Defense before the damage calc.
        // Verify the boost by comparing against the same move computed with full Defense (divisor 1),
        // both under deterministic (no-variance, no-crit) rules.
        var attacker = TestCreatures.Make("A", attack: 150);
        var defender = TestCreatures.Make("D", hp: 99999, defense: 120);

        var explosion = new Attack
        {
            Id = 5,
            Name = "Explosion",
            BaseDamage = 170,
            Accuracy = 100,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Physical,
            DamageCategory = DamageCategory.SelfDestruct,
        };

        // Reference: identical inputs but full Defense (no halving).
        int fullDefenseDamage = DamageCalculator.CalculateDamage(
            attacker,
            defender,
            explosion,
            new Gen1TypeChart(),
            NoVarianceNoCritHitRules.Instance,
            out _,
            defenseDivisor: 1
        );

        var emitter = new RecordingEmitter();
        var action = new AttackAction(
            attacker,
            defender,
            new PokemonAttack(explosion),
            new Gen1TypeChart(),
            NoVarianceNoCritHitRules.Instance,
            emitter
        );
        await action.ExecuteAsync();

        int actualDamage = emitter.Of<DamageDealt>().First().Damage;
        // Halved Defense ⇒ strictly more damage than the full-Defense reference.
        Assert.True(
            actualDamage > fullDefenseDamage,
            $"Expected halved-Defense damage ({actualDamage}) to exceed full-Defense damage ({fullDefenseDamage})."
        );
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
            Id = 6,
            Name = "SuperFang",
            BaseDamage = 1,
            Accuracy = 100,
            DamageCategory = DamageCategory.SuperFang,
        };
        attacker.AddAttack(superFang);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
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
            Id = 7,
            Name = "HyperBeam",
            BaseDamage = 150,
            Accuracy = 100,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Special,
            Effect = MoveEffect.Recharge,
        };
        attacker.AddAttack(hyperBeam);

        // Turn 1: use Hyper Beam → flag set
        var action1 = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action1.ExecuteAsync();
        Assert.True(attacker.Battle.IsRecharging);

        // Turn 2: restore defender HP so the recharge-blocked assertion is clean
        defender.Attributes.HP = defender.Attributes.MaxHP;
        int hpBefore = defender.Attributes.HP;

        var action2 = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action2.ExecuteAsync();

        Assert.False(attacker.Battle.IsRecharging); // flag cleared
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
            Id = 8,
            Name = "LeechSeed",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.LeechSeed,
        };
        attacker.AddAttack(leechSeed);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.True(defender.Battle.HasLeechSeed);
    }

    [Fact]
    public async Task LeechSeedDrain_DrainsTargetAndHealsSource()
    {
        // Player applies Leech Seed to enemy. End-of-turn drain kills the low-HP enemy
        // and heals the player.
        var player = new Creature("Player");
        player.Attributes.MaxHP = 100;
        player.Attributes.HP = 80;

        var enemy = new Creature("Enemy");
        enemy.Attributes.MaxHP = 16; // drain = max(1, 16/16) = 1 per turn
        enemy.Attributes.HP = 1; // drain on turn 1 end kills it

        var leechSeed = new Attack
        {
            Id = 8,
            Name = "LeechSeed",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.LeechSeed,
        };
        var splash = new Attack
        {
            Id = 9,
            Name = "Splash",
            BaseDamage = 0,
            Accuracy = 100,
        };
        player.AddAttack(leechSeed);
        enemy.AddAttack(splash);

        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: ConsoleBattleEventEmitter.Instance
        );
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
            Id = 10,
            Name = "Wrap",
            BaseDamage = 15,
            Accuracy = 100,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Physical,
            Effect = MoveEffect.Binding,
        };
        attacker.AddAttack(wrap);

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.True(defender.Battle.BindingTurnsRemaining > 0);
    }

    [Fact]
    public void Binding_BlocksTargetViaCanAct()
    {
        var creature = new Creature("Bound");
        creature.Battle.BindingTurnsRemaining = 3;

        bool canAct = StatusResolver.CanAct(creature, AlwaysHitRules.Instance);

        Assert.False(canAct);
        Assert.Equal(3, creature.Battle.BindingTurnsRemaining); // CanAct doesn't decrement; ApplyEndOfTurnDamage does
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
            Id = 11,
            Name = "Fly",
            BaseDamage = 70,
            Accuracy = 100,
            DamageType = DamageType.Flying,
            AttackType = AttackType.Physical,
            Effect = MoveEffect.TwoTurn,
        };
        attacker.AddAttack(fly);

        int hpBefore = defender.Attributes.HP;

        // Turn 1: charge phase — no damage, state set
        var action1 = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action1.ExecuteAsync();

        Assert.True(attacker.Battle.IsTwoTurnCharging);
        Assert.Equal(hpBefore, defender.Attributes.HP);

        // Turn 2: release phase — IsTwoTurnCharging was set, damage fires
        var action2 = new AttackAction(
            attacker,
            defender,
            attacker.Battle.ChargingMove!,
            new Gen1TypeChart(),
            AlwaysHitRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action2.ExecuteAsync();

        Assert.False(attacker.Battle.IsTwoTurnCharging);
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
            Id = 12,
            Name = "Swift",
            BaseDamage = 60,
            Accuracy = 0,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Special,
            NeverMisses = true,
        };
        attacker.AddAttack(swift);

        int hpBefore = defender.Attributes.HP;
        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            Gen1BattleRules.Instance,
            ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.True(defender.Attributes.HP < hpBefore);
    }

    [Fact]
    public void Flinch_BlocksTargetViaCanAct_AndSelfClears()
    {
        var creature = new Creature("Flinched");
        creature.Battle.IsFlinched = true;

        bool canAct = StatusResolver.CanAct(creature, AlwaysHitRules.Instance);

        Assert.False(canAct);
        Assert.False(creature.Battle.IsFlinched); // flag self-clears after blocking
    }
}
