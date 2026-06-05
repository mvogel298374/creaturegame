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
    [Fact]
    public void QuickAttackHasGen1PositivePriority() =>
        Assert.Equal(1, Move("quick-attack").Priority);

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

    /// <summary>
    /// Runs one full battle where a slow player uses <paramref name="playerMove"/> against a fast
    /// enemy's Pound, and returns the name of whoever was announced (<see cref="MoveUsed"/>) first.
    /// HP is padded so neither faints on turn 1 and both moves are observed.
    /// </summary>
    private async Task<string> FirstMoverWhenPlayerUses(string playerMove)
    {
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 999;
        player.Attributes.Attack = 40;
        player.Attributes.Speed = 1; // far slower than the enemy
        player.AddAttack(Move(playerMove));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 999;
        enemy.Attributes.Attack = 40;
        enemy.Attributes.Speed = 250; // outspeeds the player
        enemy.AddAttack(Move("pound"));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        var firstUse = emitter.Of<MoveUsed>().First();
        return firstUse.AttackerName;
    }
}
