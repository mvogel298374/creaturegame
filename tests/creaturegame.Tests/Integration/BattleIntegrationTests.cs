using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Combat;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Tests Battle.StartFightAsync() as an integrated unit: turn loop, faint detection,
/// the mid-turn dead-target guard, and the IsOutOfPP → Struggle path.
/// </summary>
public class BattleIntegrationTests
{
    [Fact]
    public async Task Battle_EndsWhenEnemyFaints()
    {
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed  = 100;
        player.AddAttack(new Attack { Name = "Slam", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP    = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical });

        var battle = new Battle(player, enemy, new Gen1TypeChart(), AutoSelectInput.Instance, AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.IsAlive());
    }

    [Fact]
    public async Task Battle_EndsWhenPlayerFaints()
    {
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.HP    = 1;
        player.Attributes.MaxHP = 1;
        player.Attributes.Speed = 1;
        player.AddAttack(new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.Attack = 999;
        enemy.Attributes.Speed  = 100;
        enemy.AddAttack(new Attack { Name = "Slam", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        var battle = new Battle(player, enemy, new Gen1TypeChart(), AutoSelectInput.Instance, AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Assert.False(player.IsAlive());
        Assert.True(enemy.IsAlive());
    }

    [Fact]
    public async Task Battle_SlowerCreature_ActionSkipped_WhenKilledFirst()
    {
        // Player goes first (speed 200 > 1) and kills enemy in one hit.
        // Enemy.Source.IsAlive() == false when its turn comes → action is skipped.
        // Enemy has attack 999: if the guard were absent it would devastate the player.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed  = 200;
        player.AddAttack(new Attack { Name = "Slam", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP     = 1;
        enemy.Attributes.MaxHP  = 1;
        enemy.Attributes.Attack = 999;
        enemy.Attributes.Speed  = 1;
        enemy.AddAttack(new Attack { Name = "Slam", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        int playerHpBefore = player.Attributes.HP;
        var battle = new Battle(player, enemy, new Gen1TypeChart(), AutoSelectInput.Instance, AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.Equal(playerHpBefore, player.Attributes.HP);
    }

    [Fact]
    public async Task Battle_UsesStruggle_WhenPPExhausted()
    {
        // Player has a 0-damage move with 1 PP — does no damage so enemy survives turn 1.
        // Enemy has 1 HP and a 0-damage move so player survives too.
        // Turn 2: Battle sees IsOutOfPP, passes null → AttackAction uses Struggle →
        // enemy (1 HP) faints and player takes recoil.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.HP     = 500;
        player.Attributes.MaxHP  = 500;
        player.Attributes.Attack = 50;
        player.Attributes.Speed  = 100;
        player.AddAttack(new Attack
        {
            Name           = "Splash",
            BaseDamage     = 0,
            Accuracy       = 100,
            AttackType     = AttackType.Physical,
            PowerPointsMax = 1,
        });

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP    = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(new Attack { Name = "Splash", BaseDamage = 0, Accuracy = 100, AttackType = AttackType.Physical });

        // Battle skips Console.ReadKey() when Console.IsInputRedirected (test context).
        var battle = new Battle(player, enemy, new Gen1TypeChart(), AutoSelectInput.Instance, AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.Attributes.HP < player.Attributes.MaxHP); // recoil damage landed
    }

    [Fact]
    public async Task Battle_PoisonedEnemy_FaintsByEndOfTurnDamage()
    {
        // Enemy has exactly 1 HP and is Poisoned — end-of-turn damage (min 1) finishes it.
        // Both creatures use 0-damage moves so no direct attack can kill either side.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Speed = 100;
        player.AddAttack(new Attack { Name = "Splash", BaseDamage = 0, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = new Creature("Enemy") { Level = 50, Status = StatusCondition.Poison };
        enemy.CalculateStats();
        enemy.Attributes.HP    = 1;
        enemy.Attributes.MaxHP = 160;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(new Attack { Name = "Splash", BaseDamage = 0, Accuracy = 100, AttackType = AttackType.Physical });

        var battle = new Battle(player, enemy, new Gen1TypeChart(), AutoSelectInput.Instance, AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.IsAlive());
    }
}
