using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Rampage moves (Thrash, Petal Dance): the first use locks the user in for 2–3 turns and the move
/// auto-repeats; when the lock expires the user confuses itself. The first test pins the lock setup
/// at the <c>AttackAction</c> level; the second drives a full <see cref="Battle"/> to prove the
/// move is force-selected on the locked turn (input is NOT consulted) and that the user ends up
/// confused — i.e. the lock actually works through the turn loop, not just in isolation.
/// </summary>
[Collection(MovesCollection.Name)]
public class RampageContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("thrash")]
    [InlineData("petal-dance")]
    public async Task FirstUseLocksTheUserInAndDealsDamage(string moveName)
    {
        var attacker = TestCreatures.Make("A");
        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 9999, defense: 200, special: 200))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>());
        // Lock was rolled at 2–3 and decremented for this turn ⇒ 1–2 turns still remain.
        Assert.InRange(result.Attacker.RampageTurnsRemaining, 1, 2);
        Assert.NotNull(result.Attacker.RampageMove);
        Assert.Equal(0, result.Attacker.ConfusedTurns);   // not confused until the lock ends
    }

    [Fact]
    public async Task LockedMoveAutoRepeatsWithoutInputThenSelfConfuses()
    {
        // Player outspeeds and Thrashes; the enemy is too tanky to die during the 2-turn lock and
        // deals no damage, so the rampage runs its course and the player confuses itself.
        var player = TestCreatures.Make("Player", attack: 60, speed: 200);
        player.AddAttack(Move("thrash"));

        var enemy = TestCreatures.Make("Enemy", hp: 600, defense: 200, speed: 1);
        enemy.AddAttack(new Attack { Name = "Splash", BaseDamage = 0, Accuracy = 100, AttackType = AttackType.Physical });

        var emitter = new RecordingEmitter();
        var input   = new RecordingInput(0);
        var battle  = new Battle(player, enemy, Gen1TypeChart.Instance, input, AutoSelectInput.Instance,
                                 rules: new FixedRampageRules(2), emitter: emitter);
        await battle.StartFightAsync();

        // Turn 1 is a free selection; turn 2 is the locked rampage turn and must NOT consult input.
        Assert.Contains(1, input.ConsultedTurns);
        Assert.DoesNotContain(2, input.ConsultedTurns);

        // The 2-turn rampage ends with the user confusing itself.
        Assert.Contains(emitter.Events, e => e is ConfusionStarted c && c.TargetName == "Player");
        Assert.True(emitter.Events.OfType<MoveUsed>().Count(m => m.AttackerName == "Player" && m.MoveName == "thrash") >= 2);
    }

    /// <summary>Records which turn numbers the engine asked for a move; always picks one fixed index.</summary>
    private sealed class RecordingInput(int index) : IBattleInput
    {
        public List<int> ConsultedTurns { get; } = [];

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
        {
            ConsultedTurns.Add(context.TurnNumber);
            return Task.FromResult(context.Attacker.MoveSet[index]);
        }
    }

    /// <summary>Always-hit, no-crit/variance rules with a fixed rampage length for determinism.</summary>
    private sealed class FixedRampageRules(int turns) : DelegatingBattleRules
    {
        public override int    RollRampageTurns()                                   => turns;
        public override int    GetHitThreshold(int acc, int accStage, int evaStage) => 256;
        public override double GetCritChance(Creature a, Attack m)                  => 0.0;
        public override double RollDamageVariance()                                 => 1.0;
    }
}
