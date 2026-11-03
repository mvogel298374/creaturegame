using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Rampage moves (Thrash, Petal Dance): the first use locks the user in for 3–4 turns and the move
/// auto-repeats; when the lock expires the user confuses itself. The first test pins the lock setup
/// at the <c>AttackAction</c> level; the others drive a full <see cref="Battle"/> to prove the
/// move is force-selected on the locked turns (input is NOT consulted) and that the user ends up
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
        // Lock was rolled at 3–4 and decremented for this turn ⇒ 2–3 turns still remain.
        Assert.InRange(result.Attacker.Battle.RampageTurnsRemaining, 2, 3);
        Assert.NotNull(result.Attacker.Battle.RampageMove);
        Assert.Equal(0, result.Attacker.Battle.ConfusedTurns); // not confused until the lock ends
    }

    [Fact]
    public async Task LockedMoveAutoRepeatsWithoutInputThenSelfConfuses()
    {
        // Player outspeeds and Thrashes; the enemy is too tanky to die during the 3-turn lock and
        // deals no damage, so the rampage runs its course and the player confuses itself.
        var (emitter, input) = await RunRampageAsync("thrash", new FixedRampageRules(3));

        // Turn 1 is a free selection; turns 2 and 3 are locked and must NOT consult input.
        Assert.Contains(1, input.ConsultedTurns);
        Assert.DoesNotContain(2, input.ConsultedTurns);
        Assert.DoesNotContain(3, input.ConsultedTurns);

        // The 3-turn rampage ends with the user confusing itself.
        Assert.Contains(emitter.Events, e => e is ConfusionStarted c && c.TargetName == "Player");
        Assert.Equal(3, AttacksBeforeConfusion(emitter, "thrash"));
    }

    // pokered: the first turn sets the counter to 2–3, and the turn it reaches zero still attacks.
    [Theory]
    [InlineData("thrash", false, 3)]
    [InlineData("thrash", true, 4)]
    [InlineData("petal-dance", false, 3)]
    [InlineData("petal-dance", true, 4)]
    public async Task RealRollAttacksForThreeOrFourTurnsThenConfuses(
        string moveName,
        bool highRoll,
        int expectedTurns
    )
    {
        IRandomSource roll = highRoll ? new HighestRollSource() : new LowestRollSource();

        var (emitter, input) = await RunRampageAsync(moveName, new RealRollRules(roll));

        Assert.Contains(emitter.Events, e => e is ConfusionStarted c && c.TargetName == "Player");
        Assert.Equal(expectedTurns, AttacksBeforeConfusion(emitter, moveName));
        Assert.Equal([1], input.ConsultedTurns.Where(turn => turn <= expectedTurns).ToArray());
        Assert.Contains(expectedTurns + 1, input.ConsultedTurns);
    }

    [Fact]
    public async Task MissedTurnsStillCountTowardTheLock()
    {
        var (emitter, _) = await RunRampageAsync(
            "thrash",
            new RealRollRules(new LowestRollSource(), hitThreshold: 0)
        );

        var beforeConfusion = emitter.Events.TakeWhile(e => e is not ConfusionStarted).ToList();
        Assert.Equal(
            3,
            beforeConfusion.OfType<MoveMissed>().Count(m => m.AttackerName == "Player")
        );
        Assert.Empty(beforeConfusion.OfType<DamageDealt>());
        Assert.Contains(emitter.Events, e => e is ConfusionStarted c && c.TargetName == "Player");
    }

    private static int AttacksBeforeConfusion(RecordingEmitter emitter, string moveName) =>
        emitter
            .Events.TakeWhile(e => e is not ConfusionStarted)
            .OfType<MoveUsed>()
            .Count(m => m.AttackerName == "Player" && m.MoveName == moveName);

    private async Task<(RecordingEmitter Emitter, RecordingInput Input)> RunRampageAsync(
        string moveName,
        IBattleRules rules
    )
    {
        var player = TestCreatures.Make("Player", attack: 60, speed: 200);
        player.AddAttack(Move(moveName));

        var enemy = TestCreatures.Make("Enemy", hp: 2000, defense: 200, special: 200, speed: 1);
        enemy.AddAttack(
            new Attack
            {
                Name = "Splash",
                BaseDamage = 0,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        var input = new RecordingInput(0);
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            input,
            AutoSelectInput.Instance,
            rules: rules,
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();
        return (emitter, input);
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
        public override int RollRampageTurns() => turns;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override double RollDamageVariance() => 1.0;
    }

    /// <summary>The real Gen 1 rampage roll over a forced random source; only accuracy, crit and variance are pinned.</summary>
    private sealed class RealRollRules(IRandomSource roll, int hitThreshold = 256)
        : DelegatingBattleRules(roll)
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => hitThreshold;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override double RollDamageVariance() => 1.0;
    }
}
