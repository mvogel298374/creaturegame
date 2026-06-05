using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Mimic (Gen 1): copies a random move from the target's set into the Mimic slot for the rest of the
/// battle, dealing no damage the turn it copies. The first test drives the copy through the real
/// <c>AttackAction</c> (a single-move target makes the random pick deterministic); the second runs a
/// full <see cref="Battle"/> to prove the copied move is then usable and that the swap <b>reverts</b>
/// at battle end — it must never leak into the permanent <see cref="Creature.MoveSet"/>.
/// </summary>
[Collection(MovesCollection.Name)]
public class MimicContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task MimicCopiesAMoveFromTheTargetWithoutDamage()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("tackle"));

        var result = await new MoveScenario()
            .Defender(defender)
            .Use(Move("mimic"));

        Assert.False(result.Has<DamageDealt>(), "Mimic deals no damage the turn it copies");
        Assert.Equal("tackle", result.Move.Base.Name);   // the Mimic slot has become Tackle

        var learned = result.First<MimicLearned>();
        Assert.NotNull(learned);
        Assert.Equal("tackle", learned!.MoveName);
    }

    [Fact]
    public async Task ResettingBattleStateRevertsAMimickedMove()
    {
        // Guards the Haze interaction: Haze calls ResetBattleState() mid-battle, which must revert the
        // transient Mimic swap rather than orphan it — otherwise the copied move leaks into the
        // permanent MoveSet (the reset throws away the bookkeeping that battle-end restore relies on).
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("tackle"));

        var result = await new MoveScenario().Defender(defender).Use(Move("mimic"));
        Assert.Equal("tackle", result.Move.Base.Name);   // swapped in

        result.Attacker.ResetBattleState();
        Assert.Equal("mimic", result.Move.Base.Name);    // reverted by the reset, not orphaned
    }

    [Fact]
    public async Task MimicRevertsToMimicWhenTheBattleEnds()
    {
        // Player leads with Mimic against an enemy whose only move is Tackle, so the copy is
        // deterministic. AutoSelect picks slot 0 each turn: turn 1 copies Tackle (no damage), turn 2
        // uses the copied Tackle to fell the enemy. When the battle ends the slot must revert to Mimic.
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 2000;
        player.Attributes.Attack = 200;
        player.Attributes.Speed  = 200;    // outspeed the enemy
        player.AddAttack(Move("mimic"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 40;
        enemy.Attributes.Defense = 20;
        enemy.Attributes.Speed   = 1;
        enemy.AddAttack(Move("tackle"));

        var emitter = new RecordingEmitter();
        var battle  = new Battle(player, enemy, Gen1TypeChart.Instance,
                                 AutoSelectInput.Instance, AutoSelectInput.Instance,
                                 rules: NoVarianceNoCritHitRules.Instance, emitter: emitter);
        await battle.StartFightAsync();

        // It copied Tackle (the enemy's only move)...
        var learned = emitter.Of<MimicLearned>().FirstOrDefault();
        Assert.NotNull(learned);
        Assert.Equal("tackle", learned!.MoveName);
        Assert.False(enemy.IsAlive());                       // and used the copy to win

        // ...and the swap reverted — the permanent move slot is Mimic again, not Tackle.
        Assert.Equal("mimic", player.MoveSet[0].Base.Name);
        Assert.Null(player.MimicWrapper);
    }
}
