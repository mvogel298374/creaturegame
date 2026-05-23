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
    private static Creature MakeCreature(string name, int hp = 100, int attack = 100,
                                         int defense = 50, int speed = 50, int level = 50)
    {
        var c = new Creature(name) { Level = level };
        c.CalculateStats();
        c.Attributes.HP     = hp;
        c.Attributes.MaxHP  = hp;
        c.Attributes.Attack = attack;
        c.Attributes.Defense = defense;
        c.Attributes.Speed  = speed;
        return c;
    }

    [Fact]
    public async Task Battle_EndsWhenEnemyFaints()
    {
        var player = MakeCreature("Player", attack: 999, speed: 100);
        player.AddAttack(new Attack { Name = "Slam", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = MakeCreature("Enemy", hp: 1, speed: 1);
        enemy.AddAttack(new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical });

        var battle = new Battle(player, enemy, new Gen1TypeChart(), AutoSelectInput.Instance, AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.IsAlive());
    }

    [Fact]
    public async Task Battle_EndsWhenPlayerFaints()
    {
        var player = MakeCreature("Player", hp: 1, speed: 1);
        player.AddAttack(new Attack { Name = "Tackle", BaseDamage = 40, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = MakeCreature("Enemy", attack: 999, speed: 100);
        enemy.AddAttack(new Attack { Name = "Slam", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        var battle = new Battle(player, enemy, new Gen1TypeChart(), AutoSelectInput.Instance, AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Assert.False(player.IsAlive());
        Assert.True(enemy.IsAlive());
    }

    [Fact]
    public async Task Battle_SlowerCreature_ActionSkipped_WhenKilledFirst()
    {
        // Player goes first (speed 200 > 1), kills enemy in one hit.
        // Enemy.Source.IsAlive() == false when its turn comes → action is skipped.
        // If the guard were absent, enemy's 999 attack would devastate player.
        var player = MakeCreature("Player", attack: 999, speed: 200);
        player.AddAttack(new Attack { Name = "Slam", BaseDamage = 100, Accuracy = 100, AttackType = AttackType.Physical });

        var enemy = MakeCreature("Enemy", hp: 1, speed: 1, attack: 999);
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
        // Player has a 0-damage move with 1 PP — does no damage to enemy so it survives turn 1.
        // Enemy has 1 HP and a 0-damage move so player survives too.
        // Turn 2: Battle sees IsOutOfPP, passes null → AttackAction uses Struggle →
        // enemy (1 HP) faints and player takes recoil.
        var player = MakeCreature("Player", hp: 500, attack: 50, defense: 50, speed: 100);
        player.Attributes.MaxHP = 500;
        player.AddAttack(new Attack
        {
            Name          = "Splash",
            BaseDamage    = 0,
            Accuracy      = 100,
            AttackType    = AttackType.Physical,
            PowerPointsMax = 1,
        });

        var enemy = MakeCreature("Enemy", hp: 1, speed: 1);
        enemy.Attributes.MaxHP = 1;
        enemy.AddAttack(new Attack { Name = "Splash", BaseDamage = 0, Accuracy = 100, AttackType = AttackType.Physical });

        // Battle skips Console.ReadKey() when Console.IsInputRedirected (test context).
        var battle = new Battle(player, enemy, new Gen1TypeChart(), AutoSelectInput.Instance, AutoSelectInput.Instance);
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.Attributes.HP < player.Attributes.MaxHP); // recoil damage landed
    }
}
