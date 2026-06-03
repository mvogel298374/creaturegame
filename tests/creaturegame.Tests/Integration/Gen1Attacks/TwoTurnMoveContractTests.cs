using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Two-turn charge moves (Razor Wind, Fly, …): turn 1 winds up and deals no damage, turn 2 lands.
/// PP is spent once (on the charge turn). The first two tests exercise the move through the engine's
/// real <c>AttackAction</c> charge/release branch; the last drives a <b>full <see cref="Battle"/></b>
/// to prove the release turn is auto-driven from <c>ChargingMove</c> without re-consulting input —
/// i.e. the mechanic works in real play, not just in isolation.
/// </summary>
[Collection(MovesCollection.Name)]
public class TwoTurnMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("razor-wind")] [InlineData("fly")]
    public async Task ChargesFirstTurnThenStrikes(string moveName)
    {
        var move = Move(moveName);
        var turns = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 9999, defense: 80, special: 80))
            .UseRepeated(move, 2);

        // Charge turn: announces the wind-up, deals no damage, doesn't "use" the move yet.
        Assert.True(turns[0].Has<ChargingUp>());
        Assert.False(turns[0].Has<DamageDealt>());
        Assert.False(turns[0].Has<MoveUsed>());

        // Release turn: the move lands and deals damage.
        Assert.True(turns[1].Has<MoveUsed>());
        Assert.True(turns[1].Has<DamageDealt>());
        Assert.True(turns[1].TotalDamage > 0);

        // PP is spent once (on the charge turn), not twice.
        Assert.Equal(move.PowerPointsMax - 1, turns[1].Move.PowerPointsCurrent);
    }

    [Theory]
    [InlineData("razor-wind")] [InlineData("fly")]
    public async Task MissesOnReleaseTurn(string moveName)
    {
        var turns = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .UseRepeated(Move(moveName), 2);

        Assert.True(turns[0].Has<ChargingUp>());
        Assert.True(turns[1].Has<MoveMissed>());
        Assert.False(turns[1].Has<DamageDealt>());
    }

    [Fact]
    public async Task ReleaseTurnIsDrivenByBattleWithoutAskingForInputAgain()
    {
        // Fly: a real two-turn move run through the full battle loop. The charge turn does no
        // damage (enemy survives); the release turn finishes the 1-HP enemy. Player input must be
        // consulted exactly once — on the charge turn — proving Battle drives the release itself
        // from Source.ChargingMove rather than asking the player to pick again.
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.Attack = 300;
        player.Attributes.Speed  = 200;   // outspeed the enemy every turn
        player.AddAttack(Move("fly"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP    = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(new Attack { Name = "Splash", BaseDamage = 0, Accuracy = 100, AttackType = AttackType.Physical });

        var emitter = new RecordingEmitter();
        var input   = new CountingInput(0);
        var battle  = new Battle(player, enemy, Gen1TypeChart.Instance, input, AutoSelectInput.Instance,
                                 rules: AlwaysHitRules.Instance, emitter: emitter);
        await battle.StartFightAsync();

        Assert.Contains(emitter.Events, e => e is ChargingUp);
        Assert.Contains(emitter.Events, e => e is DamageDealt d && d.TargetName == "Enemy");
        Assert.False(enemy.IsAlive());
        Assert.Equal(1, input.CallCount);   // asked only on the charge turn
    }

    /// <summary>Records how many times the engine asked for a move; always picks one fixed index.</summary>
    private sealed class CountingInput(int index) : IBattleInput
    {
        public int CallCount { get; private set; }

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
        {
            CallCount++;
            return Task.FromResult(context.Attacker.MoveSet[index]);
        }
    }
}
