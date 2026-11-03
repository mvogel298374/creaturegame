using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Priority moves act before lower-priority ones regardless of Speed. Quick Attack has Gen 1
/// priority +1, so a <i>slower</i> Quick Attack user still strikes before a faster foe's
/// normal-priority move. The control test confirms the harness genuinely observes turn order:
/// with a normal-priority move the slower creature moves second. Both run a full <see cref="Battle"/>
/// and read the order moves were announced in.
/// </summary>
[Collection(MovesCollection.Name)]
public class PriorityMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    private readonly MovesFixture _moves = moves;

    // pokered core.asm:371-391: Quick Attack first, Counter last, everything else by speed.
    [Fact]
    public void OnlyQuickAttackAndCounterHaveAPriority()
    {
        var withPriority = _moves
            .All.Where(m => m.Priority != 0)
            .OrderBy(m => m.Name)
            .Select(m => (m.Name, m.Priority));

        Assert.Equal([("counter", -5), ("quick-attack", 1)], withPriority);
    }

    [Theory]
    [InlineData("roar")]
    [InlineData("whirlwind")]
    [InlineData("teleport")]
    public async Task RoarWhirlwindAndTeleportGoByTheUsersOwnSpeed(string moveName)
    {
        Assert.Equal(
            "Player",
            await FirstMoverWhenPlayerUses(moveName, playerSpeed: 250, enemySpeed: 1)
        );
        Assert.Equal(
            "Enemy",
            await FirstMoverWhenPlayerUses(moveName, playerSpeed: 1, enemySpeed: 250)
        );
    }

    // pokered core.asm:375 and :386: both sides using the same exception go to the speed compare.
    [Theory]
    [InlineData("quick-attack")]
    [InlineData("counter")]
    public async Task BothSidesUsingTheSameExceptionFallBackToSpeed(string moveName)
    {
        Assert.Equal(
            "Player",
            await FirstMoverWhenPlayerUses(moveName, playerSpeed: 250, enemySpeed: 1, moveName)
        );
        Assert.Equal(
            "Enemy",
            await FirstMoverWhenPlayerUses(moveName, playerSpeed: 1, enemySpeed: 250, moveName)
        );
    }

    [Fact]
    public async Task ASlowerBideUserStoresOnlyAfterTheFasterFoeMoves()
    {
        var events = (
            await RunBattleAsync("bide", playerSpeed: 1, enemySpeed: 250)
        ).Events.ToList();

        int foeMoves = events.FindIndex(e => e is MoveUsed { AttackerName: "Enemy" });
        int bideStores = events.FindIndex(e => e is BideStoring { CreatureName: "Player" });

        Assert.InRange(foeMoves, 0, bideStores - 1);
    }

    [Fact]
    public async Task QuickAttackMovesBeforeAFasterFoesNormalMove()
    {
        // Player is far slower but uses Quick Attack (+1 priority); the faster enemy uses a
        // normal-priority move. The player must still be announced first.
        var firstMover = await FirstMoverWhenPlayerUses("quick-attack");
        Assert.Equal("Player", firstMover);
    }

    [Fact]
    public async Task WithoutPriorityTheFasterFoeMovesFirst()
    {
        // Control: same speed disadvantage, but a normal-priority move (Pound). Now Speed decides and
        // the faster enemy is announced first — proving the test above isn't trivially always-player.
        var firstMover = await FirstMoverWhenPlayerUses("pound");
        Assert.Equal("Enemy", firstMover);
    }

    /// <summary>Who was announced (<see cref="MoveUsed"/>) first; the player is far slower by default.</summary>
    private async Task<string> FirstMoverWhenPlayerUses(
        string playerMove,
        int playerSpeed = 1,
        int enemySpeed = 250,
        string enemyMove = "pound"
    )
    {
        var emitter = await RunBattleAsync(playerMove, playerSpeed, enemySpeed, enemyMove);
        return emitter.Of<MoveUsed>().First().AttackerName;
    }

    /// <summary>One full battle; HP is padded so neither faints on turn 1.</summary>
    private async Task<RecordingEmitter> RunBattleAsync(
        string playerMove,
        int playerSpeed,
        int enemySpeed,
        string enemyMove = "pound"
    )
    {
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 999;
        player.Attributes.Attack = 40;
        player.Attributes.Speed = playerSpeed;
        player.AddAttack(Move(playerMove));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 999;
        enemy.Attributes.Attack = 40;
        enemy.Attributes.Speed = enemySpeed;
        enemy.AddAttack(Move(enemyMove));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();
        return emitter;
    }
}
