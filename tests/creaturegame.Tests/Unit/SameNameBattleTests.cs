using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Creature Identity (<c>ARCHITECTURE.md</c> §2.2): two combatants can share a display name — a wild PIDGEY against the
/// player's un-nicknamed PIDGEY. The engine leaves both names alone and tells the sides apart by id, which the
/// client routes on. (An earlier stop-gap renamed the enemy to "Enemy PIDGEY" for the fight; ids replaced it.)
/// </summary>
public class SameNameBattleTests
{
    private static Creature Make(string name, int hp, int speed)
    {
        var c = new Creature(name) { Level = 50 };
        c.CalculateStats();
        c.Attributes.HP = hp;
        c.Attributes.MaxHP = hp;
        c.Attributes.Speed = speed;
        c.Attributes.Attack = 999;
        c.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 100,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );
        return c;
    }

    private static async Task<RecordingEmitter> FightAsync(Creature player, Creature enemy)
    {
        var recorder = new RecordingEmitter();
        await new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: recorder
        ).StartFightAsync();
        return recorder;
    }

    [Fact]
    public async Task SameNamedCombatants_KeepTheirNames_AndEveryEventIdentifiesItsCreatureById()
    {
        var ids = new CreatureIdSource();
        var player = ids.Assign(Make("PIDGEY", hp: 500, speed: 100));
        var enemy = ids.Assign(Make("PIDGEY", hp: 1, speed: 1));

        var recorder = await FightAsync(player, enemy);

        var started = Assert.Single(recorder.Of<BattleStarted>());
        Assert.Equal("PIDGEY", started.PlayerName);
        Assert.Equal("PIDGEY", started.EnemyName); // names are display text: identical, and left alone
        Assert.Equal("PIDGEY", enemy.Name);
        Assert.Equal(player.Id, started.PlayerId);
        Assert.Equal(enemy.Id, started.EnemyId);
        Assert.NotEqual(started.PlayerId, started.EnemyId);

        var turn = recorder.Of<TurnStarted>().First();
        Assert.Equal(player.Id, turn.PlayerId);
        Assert.Equal(enemy.Id, turn.EnemyId);

        // The player's Tackle is the only attack that lands: the hit is on the enemy, and the faint is the enemy's.
        Assert.Equal(player.Id, recorder.Of<MoveUsed>().First().AttackerId);
        Assert.Equal(enemy.Id, recorder.Of<DamageDealt>().First().TargetId);
        Assert.Equal(enemy.Id, Assert.Single(recorder.Of<CreatureFainted>()).Id);
        Assert.Equal(player.Id, Assert.Single(recorder.Of<BattleEnded>()).WinnerId);
    }

    [Fact]
    public async Task ADifferentlyNamedEnemy_IsIdentifiedByIdToo()
    {
        var ids = new CreatureIdSource();
        var player = ids.Assign(Make("PIDGEY", hp: 500, speed: 100));
        var enemy = ids.Assign(Make("RATTATA", hp: 1, speed: 1));

        var recorder = await FightAsync(player, enemy);

        var started = Assert.Single(recorder.Of<BattleStarted>());
        Assert.Equal("RATTATA", started.EnemyName);
        Assert.Equal(enemy.Id, started.EnemyId);
    }
}
