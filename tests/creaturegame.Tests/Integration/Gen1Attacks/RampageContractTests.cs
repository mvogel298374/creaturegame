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
        Assert.Contains(emitter.Events, IsPlayerConfusionMessage);
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

        Assert.Contains(emitter.Events, IsPlayerConfusionMessage);
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

        var beforeConfusion = BeforeFirstConfusionMessage(emitter);
        Assert.Equal(
            3,
            beforeConfusion.OfType<MoveMissed>().Count(m => m.AttackerName == "Player")
        );
        Assert.Empty(beforeConfusion.OfType<DamageDealt>());
        Assert.Contains(emitter.Events, IsPlayerConfusionMessage);
    }

    // pokered .ThrashingAboutCheck prints ThrashingAboutText and jumps past DisplayUsedMoveText.
    [Theory]
    [InlineData("thrash")]
    [InlineData("petal-dance")]
    public async Task RepeatTurnsSayThrashingAboutInsteadOfUsed(string moveName)
    {
        var (emitter, _) = await RunRampageAsync(moveName, new FixedRampageRules(3));

        var lockTurns = BeforeFirstConfusionMessage(emitter);
        Assert.Single(lockTurns.OfType<MoveUsed>(), m => m.AttackerName == "Player");
        var repeats = lockTurns.OfType<RampageContinues>().ToList();
        Assert.Equal(2, repeats.Count);
        Assert.All(
            repeats,
            r =>
            {
                Assert.Equal("Player", r.CreatureName);
                Assert.Equal(moveName, r.MoveName);
            }
        );
        Assert.Equal(3, lockTurns.OfType<DamageDealt>().Count(d => d.TargetName == "Enemy"));
    }

    [Fact]
    public async Task LockEndConfusesTheUserWithoutAnnouncingIt()
    {
        var (emitter, _) = await RunRampageAsync("thrash", new FixedRampageRules(3));

        Assert.DoesNotContain(emitter.Events, e => e is ConfusionStarted);
        Assert.Contains(emitter.Events, IsPlayerConfusionMessage);
    }

    [Fact]
    public async Task RepeatTurnStillRecordsTheMoveForMirrorMove()
    {
        var attacker = TestCreatures.Make("A");
        attacker.Battle.RampageTurnsRemaining = 2;
        attacker.Battle.RampageMove = new PokemonAttack(Move("thrash"));
        attacker.Battle.LastMoveUsed = Move("tackle");

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 9999))
            .Use(Move("thrash"));

        Assert.True(result.Has<RampageContinues>());
        Assert.False(result.Has<MoveUsed>());
        Assert.Equal("thrash", result.Attacker.Battle.LastMoveUsed?.Name);
    }

    // pokered core.asm:3545-3550 writes a fresh 2-5 counter even over a running one.
    [Fact]
    public async Task AlreadyConfusedUserGetsAFreshCounterAtLockEnd()
    {
        // Confuse Ray on turn 1 rolls 5; turns 2 and 3 tick it to 3; the lock's end rolls 2.
        var (emitter, _) = await RunRampageAsync(
            "thrash",
            new ScriptedConfusionRules(3, 5, 2),
            Move("confuse-ray")
        );

        int lockEnd = PlayerAttackIndices(emitter)[2];
        int confusedTurnsAfterLock = emitter
            .Events.Skip(lockEnd + 1)
            .TakeWhile(e => e is not ConfusionCleared { CreatureName: "Player" })
            .Count(IsPlayerConfusionMessage);
        Assert.Equal(2, confusedTurnsAfterLock); // the kept counter would have lasted 3
    }

    [Fact]
    public async Task UsedMoveSettingAnnouncesEveryRampageTurnAsUsed()
    {
        var (emitter, _) = await RunRampageAsync("petal-dance", AltRules());

        Assert.DoesNotContain(emitter.Events, e => e is RampageContinues);
        Assert.InRange(
            emitter
                .Events.TakeWhile(e => e is not ConfusionStarted)
                .OfType<MoveUsed>()
                .Count(m => m.AttackerName == "Player"),
            3,
            4
        );
    }

    [Fact]
    public async Task AnnouncingSettingPrintsConfusionAtTheLockEnd()
    {
        var (emitter, _) = await RunRampageAsync("thrash", AltRules());

        var events = emitter.Events.ToList();
        int started = events.FindIndex(e => e is ConfusionStarted { TargetName: "Player" });
        Assert.True(started >= 0);
        Assert.True(started < events.FindIndex(IsPlayerConfusionMessage));
    }

    [Fact]
    public async Task NoOverwriteSettingKeepsARunningConfusionCounterAtLockEnd()
    {
        // Confuse Ray on turn 1 rolls 5; turns 2 and 3 tick it to 3; the lock's end keeps it.
        var (emitter, _) = await RunRampageAsync(
            "thrash",
            new KeepCounterScriptedRules(3, 5, 2),
            Move("confuse-ray")
        );

        int lockEnd = PlayerAttackIndices(emitter)[2];
        var afterLock = emitter
            .Events.Skip(lockEnd + 1)
            .TakeWhile(e => e is not ConfusionCleared { CreatureName: "Player" })
            .ToList();
        Assert.Equal(3, afterLock.Count(IsPlayerConfusionMessage)); // a fresh roll would have lasted 2
        Assert.DoesNotContain(afterLock, e => e is ConfusionStarted);
    }

    private static IBattleRules AltRules() =>
        TestAltProfile.Instance.BuildBattleRules(new SeededRandomSource(0));

    private static bool IsPlayerConfusionMessage(BattleEvent e) =>
        e is ConfusionMessage { CreatureName: "Player" };

    private static List<BattleEvent> BeforeFirstConfusionMessage(RecordingEmitter emitter) =>
        emitter.Events.TakeWhile(e => !IsPlayerConfusionMessage(e)).ToList();

    private static List<int> PlayerAttackIndices(RecordingEmitter emitter) =>
        emitter
            .Events.Select((e, i) => (e, i))
            .Where(x =>
                x.e
                    is MoveUsed { AttackerName: "Player" }
                        or RampageContinues { CreatureName: "Player" }
            )
            .Select(x => x.i)
            .ToList();

    private static int AttacksBeforeConfusion(RecordingEmitter emitter, string moveName) =>
        BeforeFirstConfusionMessage(emitter)
            .Count(e =>
                (e is MoveUsed { AttackerName: "Player" } m && m.MoveName == moveName)
                || (e is RampageContinues { CreatureName: "Player" } r && r.MoveName == moveName)
            );

    private async Task<(RecordingEmitter Emitter, RecordingInput Input)> RunRampageAsync(
        string moveName,
        IBattleRules rules,
        Attack? enemyMove = null
    )
    {
        var player = TestCreatures.Make("Player", attack: 60, speed: 200);
        player.AddAttack(Move(moveName));

        var enemy = TestCreatures.Make("Enemy", hp: 2000, defense: 200, special: 200, speed: 1);
        enemy.AddAttack(
            enemyMove
                ?? new Attack
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

    /// <summary>A fixed rampage length, scripted confusion rolls (the last repeats) and no confusion self-hit.</summary>
    private class ScriptedConfusionRules(int rampageTurns, params int[] confusionRolls)
        : DelegatingBattleRules
    {
        private int _rolls;

        public override int RollRampageTurns() => rampageTurns;

        public override int RollConfusionTurns() =>
            confusionRolls[Math.Min(_rolls++, confusionRolls.Length - 1)];

        public override int ConfusionSelfHitPercent => 0;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override double RollDamageVariance() => 1.0;
    }

    private sealed class KeepCounterScriptedRules(int rampageTurns, params int[] confusionRolls)
        : ScriptedConfusionRules(rampageTurns, confusionRolls)
    {
        public override bool RampageEndOverwritesConfusion => false;
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
