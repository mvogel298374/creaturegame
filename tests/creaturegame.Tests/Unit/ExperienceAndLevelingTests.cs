using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class ExperienceAndLevelingTests
{
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
            GrowthRate = GrowthRate.MediumFast,
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
    public async Task XP_AwardedToWinnerOnEnemyFaint()
    {
        // Charmander base experience = 64; at level 50, Gen 1 wild formula:
        //   floor(64 × 50 / 7) = 457 XP awarded to the winning player.
        int enemyBaseExp = 64;
        int enemyLevel = 50;
        int expectedXP = (int)Math.Floor((double)enemyBaseExp * enemyLevel / 7); // 457

        Console.WriteLine("--- XP Award: Enemy Faints ---");
        Console.WriteLine($"Enemy: Charmander Lv{enemyLevel}, BaseExp={enemyBaseExp}");
        Console.WriteLine($"Formula: floor({enemyBaseExp} × {enemyLevel} / 7) = {expectedXP} XP");

        var player = new Creature("Bulbasaur") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 100,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Charmander")
        {
            Level = enemyLevel,
            SpeciesBaseExperience = enemyBaseExp,
        };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Scratch",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        Console.WriteLine($"Player XP before battle: {player.Experience}");

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
            Level = 1,
            Experience = 0,
            GrowthRate = GrowthRate.MediumFast,
            BaseHP = 45,
        };
        creature.CalculateStats();

        Console.WriteLine($"Before: Level {creature.Level}, XP {creature.Experience}");
        creature.GainExperience(10);
        Console.WriteLine($"After +10 XP: Level {creature.Level}, XP {creature.Experience}");
        Console.WriteLine("(XP accumulates; level counter steps forward past each threshold)");

        Assert.Equal(2, creature.Level);
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
        int enemyLevel = 1;
        int expectedXP = (int)Math.Floor((double)enemyBaseExp * enemyLevel / 7); // 9

        Console.WriteLine("--- LeveledUp Event Fires ---");
        Console.WriteLine(
            $"Enemy: Rattata Lv{enemyLevel}, BaseExp={enemyBaseExp} → awards {expectedXP} XP"
        );
        Console.WriteLine("Player starts Lv1, MediumFast — Lv2 threshold = 8 XP");
        Console.WriteLine(
            $"{expectedXP} XP >= 8 → player reaches Lv2; {expectedXP} XP < 27 → stops there"
        );

        var player = new Creature("Bulbasaur") { Level = 1, GrowthRate = GrowthRate.MediumFast };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 100,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Rattata")
        {
            Level = enemyLevel,
            SpeciesBaseExperience = enemyBaseExp,
        };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Scratch",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var recorder = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: recorder
        );
        await battle.StartFightAsync();

        var levelUps = recorder.Of<LeveledUp>().ToList();
        Console.WriteLine($"LeveledUp events captured: {levelUps.Count}");
        foreach (var e in levelUps)
            Console.WriteLine($"  → {e.CreatureName} grew to level {e.NewLevel}!");

        Assert.Single(levelUps);
        Assert.Equal("Bulbasaur", levelUps[0].CreatureName);
        Assert.Equal(2, levelUps[0].NewLevel);
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
        player.Attributes.HP = 1;
        player.Attributes.MaxHP = 1;
        player.Attributes.Speed = 1;
        player.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Charmander") { Level = 50, SpeciesBaseExperience = 64 };
        enemy.CalculateStats();
        enemy.Attributes.Attack = 999;
        enemy.Attributes.Speed = 100;
        enemy.AddAttack(
            new Attack
            {
                Name = "Flamethrower",
                BaseDamage = 100,
                Accuracy = 100,
                AttackType = AttackType.Special,
            }
        );

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
            Id = 1,
            Name = "bulbasaur",
            BaseHP = 45,
            BaseAttack = 49,
            BaseDefense = 49,
            BaseSpecial = 65,
            BaseSpeed = 45,
            GrowthRate = GrowthRate.MediumFast,
            BaseExperience = 64,
        };

        int level = 30;
        var creature = new Creature("Bulbasaur") { Level = level };
        creature.InitializeFromSpecies(species);
        creature.Experience = creature.CalculateExperienceForLevel(level);

        Console.WriteLine(
            $"Level: {creature.Level}   XP: {creature.Experience}   "
                + $"HP: {creature.Attributes.HP}   ATK: {creature.Attributes.Attack}   "
                + $"BaseExp: {creature.SpeciesBaseExperience}"
        );

        Assert.Equal(30, creature.Level);
        Assert.Equal(27_000, creature.Experience);
        Assert.Equal(64, creature.SpeciesBaseExperience);
        // HP at Lv30 for Bulbasaur: range [67, 76] — well below Lv50 range [128, 143]
        Assert.InRange(creature.Attributes.HP, 60, 100);
        // Attack at Lv30: range [29, 43] — below Lv50 min of 54
        Assert.InRange(creature.Attributes.Attack, 25, 50);
    }
}
