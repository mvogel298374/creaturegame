using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>The checks a creature runs before it moves (GENERATION_SEAMS.md §2, Pre-move checks).</summary>
[Collection(MovesCollection.Name)]
public class PreMoveCheckContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    private static (bool Acted, RecordingEmitter Log) Check(
        Creature creature,
        IRandomSource? rng = null,
        PokemonAttack? selected = null,
        IBattleRules? rules = null
    )
    {
        var log = new RecordingEmitter();
        bool acted = StatusResolver.CanAct(
            creature,
            rules ?? Gen1BattleRules.Instance,
            log,
            rng ?? new HighestRollSource(),
            TestCreatures.Make("Foe"),
            selected
        );
        return (acted, log);
    }

    private Creature WithDisabledPound(int turns = 3)
    {
        var creature = TestCreatures.Make("Mon");
        creature.AddAttack(Move("pound"));
        creature.Battle.DisabledMove = creature.MoveSet[0];
        creature.Battle.DisableTurnsRemaining = turns;
        return creature;
    }

    private static void Lose(Creature creature, PreMoveCheck check)
    {
        switch (check)
        {
            case PreMoveCheck.Sleep:
                creature.Battle.Status = StatusCondition.Sleep;
                creature.Battle.SleepTurns = 3;
                break;
            case PreMoveCheck.Freeze:
                creature.Battle.Status = StatusCondition.Freeze;
                break;
            case PreMoveCheck.HeldByTrap:
                creature.Battle.BindingTurnsRemaining = 3;
                break;
            case PreMoveCheck.Flinch:
                creature.Battle.IsFlinched = true;
                break;
            case PreMoveCheck.Recharge:
                creature.Battle.IsRecharging = true;
                break;
            case PreMoveCheck.Paralysis:
                creature.Battle.Status = StatusCondition.Paralysis;
                break;
            case PreMoveCheck.Confusion:
                creature.Battle.ConfusedTurns = 3;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(check), check, null);
        }
    }

    private (Creature Binder, Creature Victim) LockedIntoEverything()
    {
        var binder = TestCreatures.Make("Binder");
        var victim = TestCreatures.Make("Victim");
        binder.AddAttack(Move("thrash"));
        var move = binder.MoveSet[0];
        binder.Battle.BideTurnsRemaining = 2;
        binder.Battle.BideDamageAccumulated = 40;
        binder.Battle.BideMove = move;
        binder.Battle.RampageTurnsRemaining = 2;
        binder.Battle.RampageMove = move;
        binder.Battle.IsTwoTurnCharging = true;
        binder.Battle.ChargingMove = move;
        binder.Battle.BindingMove = move;
        binder.Battle.BindingTarget = victim;
        victim.Battle.BindingTurnsRemaining = 3;
        return (binder, victim);
    }

    // ── The order ────────────────────────────────────────────────────────────

    [Fact]
    public void SleepTicksWhileTheCreatureIsHeldByATrap()
    {
        var creature = TestCreatures.Make("Mon");
        Lose(creature, PreMoveCheck.Sleep);
        Lose(creature, PreMoveCheck.HeldByTrap);

        var (acted, log) = Check(creature);

        Assert.False(acted);
        Assert.Equal(2, creature.Battle.SleepTurns);
        Assert.Empty(log.Of<BindingBlocked>());
    }

    [Fact]
    public void FreezeStopsTheTurnBeforeTheTrapAndLeavesARechargePending()
    {
        var creature = TestCreatures.Make("Mon");
        Lose(creature, PreMoveCheck.Freeze);
        Lose(creature, PreMoveCheck.HeldByTrap);
        Lose(creature, PreMoveCheck.Recharge);

        var (_, log) = Check(creature);

        Assert.Single(log.Of<ActionBlocked>());
        Assert.Empty(log.Of<BindingBlocked>());
        Assert.True(creature.Battle.IsRecharging);
    }

    [Fact]
    public void TheTrapStopsTheTurnBeforeFlinch()
    {
        var creature = TestCreatures.Make("Mon");
        Lose(creature, PreMoveCheck.HeldByTrap);
        Lose(creature, PreMoveCheck.Flinch);

        var (_, log) = Check(creature);

        Assert.Single(log.Of<BindingBlocked>());
        Assert.Empty(log.Of<FlinchBlocked>());
    }

    [Fact]
    public void FlinchStopsTheTurnBeforeRecharge()
    {
        var creature = TestCreatures.Make("Mon");
        Lose(creature, PreMoveCheck.Flinch);
        Lose(creature, PreMoveCheck.Recharge);

        var (_, log) = Check(creature);

        Assert.Single(log.Of<FlinchBlocked>());
        Assert.Empty(log.Of<Recharging>());
        Assert.True(creature.Battle.IsRecharging);
    }

    [Fact]
    public void RechargeSpendsTheTurnWithoutRollingParalysis()
    {
        var creature = WithDisabledPound();
        Lose(creature, PreMoveCheck.Recharge);
        Lose(creature, PreMoveCheck.Paralysis);

        var (acted, log) = Check(creature, new LowestRollSource());

        Assert.False(acted);
        Assert.Single(log.Of<Recharging>());
        Assert.Empty(log.Of<ActionBlocked>());
    }

    [Fact]
    public void AConfusionSelfHitStopsTheTurnBeforeTheDisabledMoveAndParalysisChecks()
    {
        var creature = WithDisabledPound();
        Lose(creature, PreMoveCheck.Confusion);
        Lose(creature, PreMoveCheck.Paralysis);

        var (_, log) = Check(creature, new LowestRollSource(), creature.MoveSet[0]);

        Assert.Single(log.Of<ConfusionDamage>());
        Assert.Empty(log.Of<MoveIsDisabled>());
        Assert.Empty(log.Of<ActionBlocked>());
    }

    [Fact]
    public void SnappingOutOfConfusionStillRunsTheParalysisCheck()
    {
        var creature = TestCreatures.Make("Mon");
        creature.Battle.ConfusedTurns = 1;
        Lose(creature, PreMoveCheck.Paralysis);

        var (acted, log) = Check(creature, new LowestRollSource());

        Assert.False(acted);
        Assert.Single(log.Of<ConfusionCleared>());
        Assert.Single(log.Of<ActionBlocked>());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task TheTurnConfusionEndsPrintsIsConfusedOnlyWhenTheSeamSaysSo(
        bool printsConfusedLine,
        int confusedLines
    )
    {
        var player = TestCreatures.Make("Player", hp: 300, attack: 50, speed: 1);
        player.AddAttack(Move("tackle"));
        var enemy = TestCreatures.Make("Enemy", hp: 9999, attack: 200, speed: 200);
        enemy.AddAttack(Move("confuse-ray"));
        enemy.AddAttack(Move("tackle"));

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("tackle")
            .EnemyUses("confuse-ray", "tackle")
            .Rules(new TwoTurnConfusionRules(printsConfusedLine))
            .RunAsync();

        var events = result.Events.ToList();
        int cleared = events.FindIndex(e => e is ConfusionCleared { CreatureName: "Player" });
        Assert.Equal(
            confusedLines,
            result.All<ConfusionMessage>().Count(m => m.CreatureName == "Player")
        );
        Assert.Single(result.All<ConfusionCleared>());
        Assert.Equal(printsConfusedLine, events[cleared - 1] is ConfusionMessage);
        Assert.IsType<MoveUsed>(events[cleared + 1]);
    }

    private sealed class TwoTurnConfusionRules(bool printsConfusedLine) : DelegatingBattleRules
    {
        public override bool ConfusionEndTurnPrintsConfusedLine => printsConfusedLine;

        public override int RollConfusionTurns() => 2;

        public override int ConfusionSelfHitPercent => 0;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature attacker, Attack move) => 0;
    }

    [Fact]
    public void UsingADisabledMoveStopsTheTurnBeforeParalysis()
    {
        var creature = WithDisabledPound();
        Lose(creature, PreMoveCheck.Paralysis);

        var (_, log) = Check(creature, new LowestRollSource(), creature.MoveSet[0]);

        Assert.Single(log.Of<MoveIsDisabled>());
        Assert.Empty(log.Of<ActionBlocked>());
    }

    // ── Disable ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PreMoveCheck.Sleep)]
    [InlineData(PreMoveCheck.Freeze)]
    [InlineData(PreMoveCheck.HeldByTrap)]
    [InlineData(PreMoveCheck.Flinch)]
    [InlineData(PreMoveCheck.Recharge)]
    public void DisableDoesNotCountDownOnATurnLostBeforeItsCheck(PreMoveCheck check)
    {
        var creature = WithDisabledPound();
        Lose(creature, check);

        Check(creature);

        Assert.Equal(3, creature.Battle.DisableTurnsRemaining);
    }

    [Fact]
    public void ARollOfThreeLastsThreeOfTheCreaturesOwnAttempts()
    {
        var creature = WithDisabledPound();

        Check(creature);
        Assert.Equal(2, creature.Battle.DisableTurnsRemaining);
        Check(creature);
        Assert.Equal(1, creature.Battle.DisableTurnsRemaining);
        Assert.NotNull(creature.Battle.DisabledMove);

        var (_, log) = Check(creature);

        Assert.Single(log.Of<MoveReEnabled>());
        Assert.Null(creature.Battle.DisabledMove);
    }

    [Fact]
    public void TheMoveGoesThroughOnTheAttemptWhoseCountdownExpires()
    {
        var creature = WithDisabledPound(turns: 1);

        var (acted, log) = Check(creature, selected: creature.MoveSet[0]);

        Assert.True(acted);
        Assert.Single(log.Of<MoveReEnabled>());
        Assert.Empty(log.Of<MoveIsDisabled>());
    }

    [Fact]
    public async Task AMoveDisabledAfterItWasChosenPrintsIsDisabledAndIsNotUsed()
    {
        var player = TestCreatures.Make("Player", hp: 99999, attack: 150, speed: 1);
        player.AddAttack(Move("pound"));
        var enemy = TestCreatures.Make("Enemy", hp: 500, defense: 60, speed: 200);
        enemy.AddAttack(Move("disable"));

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("pound")
            .EnemyUses("disable")
            .Rules(new ScriptableRules().Deterministic().DisableTurns(3))
            .RunAsync();

        var blocked = result.First<MoveIsDisabled>();
        Assert.NotNull(blocked);
        Assert.Equal("pound", blocked!.MoveName);
        Assert.Equal("Player", blocked.CreatureName);
        Assert.DoesNotContain(
            result.All<MoveUsed>(),
            m => m.AttackerName == "Player" && m.MoveName == "pound"
        );
    }

    [Fact]
    public void BeingStoppedFromADisabledMoveEndsTheChargeButNotARampage()
    {
        var creature = WithDisabledPound();
        var pound = creature.MoveSet[0];
        creature.Battle.IsTwoTurnCharging = true;
        creature.Battle.ChargingMove = pound;
        creature.Battle.RampageTurnsRemaining = 2;
        creature.Battle.RampageMove = pound;

        Check(creature, selected: pound);

        Assert.False(creature.Battle.IsTwoTurnCharging);
        Assert.Null(creature.Battle.ChargingMove);
        Assert.Equal(2, creature.Battle.RampageTurnsRemaining);
    }

    // ── Cancelled lock-ins ───────────────────────────────────────────────────

    [Theory]
    [InlineData(PreMoveCheck.Paralysis)]
    [InlineData(PreMoveCheck.Confusion)]
    public void ParalysisAndAConfusionSelfHitEndEveryLockInAndFreeTheFoe(PreMoveCheck check)
    {
        var (binder, victim) = LockedIntoEverything();
        Lose(binder, check);

        var (acted, _) = Check(binder, new LowestRollSource());

        Assert.False(acted);
        Assert.Equal(0, binder.Battle.BideTurnsRemaining);
        Assert.Equal(0, binder.Battle.BideDamageAccumulated);
        Assert.Null(binder.Battle.BideMove);
        Assert.Equal(0, binder.Battle.RampageTurnsRemaining);
        Assert.Null(binder.Battle.RampageMove);
        Assert.False(binder.Battle.IsTwoTurnCharging);
        Assert.Null(binder.Battle.ChargingMove);
        Assert.Null(binder.Battle.BindingMove);
        Assert.Null(binder.Battle.BindingTarget);
        Assert.Equal(0, victim.Battle.BindingTurnsRemaining);
    }

    [Theory]
    [InlineData(PreMoveCheck.Sleep)]
    [InlineData(PreMoveCheck.Freeze)]
    [InlineData(PreMoveCheck.Flinch)]
    [InlineData(PreMoveCheck.Recharge)]
    public void OtherLostTurnsKeepTheLockIn(PreMoveCheck check)
    {
        var (binder, victim) = LockedIntoEverything();
        Lose(binder, check);

        Check(binder, new LowestRollSource());

        Assert.Equal(2, binder.Battle.RampageTurnsRemaining);
        Assert.True(binder.Battle.IsTwoTurnCharging);
        Assert.Equal(3, victim.Battle.BindingTurnsRemaining);
    }

    [Fact]
    public async Task AThrashCutShortByFullParalysisLeavesTheUserUnconfused()
    {
        var player = TestCreatures.Make("Player", hp: 300, attack: 50, speed: 200);
        player.AddAttack(Move("thrash"));
        var enemy = TestCreatures.Make("Enemy", hp: 9999, attack: 200, speed: 1);
        enemy.AddAttack(Move("thunder-wave"));
        enemy.AddAttack(Move("tackle"));

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("thrash")
            .EnemyUses("thunder-wave", "tackle")
            .Rules(new ScriptableRules().Deterministic().RampageTurns(3))
            .BattleRng(new LowestRollSource())
            .RunAsync();

        Assert.Single(result.All<MoveUsed>(), m => m.MoveName == "thrash");
        Assert.Contains(
            result.All<ActionBlocked>(),
            b => b.Reason == StatusCondition.Paralysis && b.CreatureName == "Player"
        );
        Assert.Empty(result.All<RampageContinues>());
        Assert.Empty(result.All<ConfusionMessage>());
    }

    [Fact]
    public async Task FullParalysisOnTheBinderFreesTheFoeOnTheNextTurn()
    {
        var player = TestCreatures.Make("Player", hp: 300, attack: 50, speed: 1);
        player.AddAttack(Move("wrap"));
        var enemy = TestCreatures.Make("Enemy", hp: 9999, attack: 200, speed: 200);
        enemy.AddAttack(Move("thunder-wave"));
        enemy.AddAttack(Move("tackle"));

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("wrap")
            .EnemyUses("thunder-wave", "tackle")
            .Rules(new FullyParalyzedFromSecondAttempt())
            .BattleRng(new LowestRollSource())
            .RunAsync();

        Assert.Single(result.All<BindingStarted>());
        Assert.Single(result.All<BindingBlocked>());
        Assert.Contains(
            result.All<MoveUsed>(),
            m => m.AttackerName == "Enemy" && m.MoveName == "tackle"
        );
    }

    private sealed class FullyParalyzedFromSecondAttempt : DelegatingBattleRules
    {
        private int _attempts;

        public override bool RollsFullParalysis(IRandomSource rng) => ++_attempts >= 2;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature attacker, Attack move) => 0;

        public override int RollBindingTurns() => 5;
    }

    [Fact]
    public void AFullyParalyzedConfusedCreatureStillCountsItsConfusionDown()
    {
        var creature = TestCreatures.Make("Mon");
        Lose(creature, PreMoveCheck.Confusion);
        Lose(creature, PreMoveCheck.Paralysis);

        var (acted, log) = Check(
            creature,
            new LowestRollSource(),
            rules: new ConfusionNeverHitsItself()
        );

        Assert.False(acted);
        Assert.Equal(2, creature.Battle.ConfusedTurns);
        Assert.Single(log.Of<ActionBlocked>());
    }

    private sealed class ConfusionNeverHitsItself : DelegatingBattleRules
    {
        public override int ConfusionSelfHitPercent => 0;
    }

    // ── The paralysis roll ───────────────────────────────────────────────────

    private sealed class FixedRoll(int value) : IRandomSource
    {
        public int Next(int maxExclusive) => value;

        public int Next(int minInclusive, int maxExclusive) => value;

        public double NextDouble() => 0.0;
    }

    [Theory]
    [InlineData(62, false)]
    [InlineData(63, true)]
    public void FullParalysisIsARollBelowSixtyThreeOutOf256(int roll, bool acts)
    {
        var creature = TestCreatures.Make("Mon");
        Lose(creature, PreMoveCheck.Paralysis);

        var (acted, _) = Check(creature, new FixedRoll(roll));

        Assert.Equal(acts, acted);
    }

    [Fact]
    public void FullParalysisDrawsOnceFromAScaleOf256()
    {
        var creature = TestCreatures.Make("Mon");
        Lose(creature, PreMoveCheck.Paralysis);
        var rng = new RecordingRandomSource(new HighestRollSource());

        Check(creature, rng);

        Assert.Equal(["Next(256)"], rng.Draws);
    }

    // ── Mirror Move and a sleep or frozen turn ───────────────────────────────

    [Theory]
    [InlineData(PreMoveCheck.Sleep, 3)]
    [InlineData(PreMoveCheck.Sleep, 1)]
    [InlineData(PreMoveCheck.Freeze, 0)]
    public void ASleepOrFrozenTurnErasesTheLastMove(PreMoveCheck check, int sleepTurns)
    {
        var creature = TestCreatures.Make("Mon");
        creature.Battle.LastMoveUsed = Move("tackle");
        Lose(creature, check);
        creature.Battle.SleepTurns = sleepTurns;

        Check(creature);

        Assert.Null(creature.Battle.LastMoveUsed);
    }

    [Theory]
    [InlineData(PreMoveCheck.Flinch)]
    [InlineData(PreMoveCheck.Recharge)]
    [InlineData(PreMoveCheck.Paralysis)]
    public void OtherLostTurnsKeepTheLastMove(PreMoveCheck check)
    {
        var creature = TestCreatures.Make("Mon");
        creature.Battle.LastMoveUsed = Move("tackle");
        Lose(creature, check);

        Check(creature, new LowestRollSource());

        Assert.NotNull(creature.Battle.LastMoveUsed);
    }

    [Fact]
    public async Task MirrorMoveFailsAgainstAFoeThatSpentItsLastTurnAsleep()
    {
        var player = TestCreatures.Make("Player", hp: 9999, attack: 500, speed: 1);
        player.AddAttack(Move("hypnosis"));
        player.AddAttack(Move("mirror-move"));
        var enemy = TestCreatures.Make("Enemy", hp: 100, attack: 10, speed: 200);
        enemy.AddAttack(Move("tackle"));

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("hypnosis", "mirror-move")
            .EnemyUses("tackle")
            .Rules(new ScriptableRules().Deterministic().SleepTurns(2))
            .RunAsync();

        // Asleep on turn 2, awake but moveless on turn 3; the copy works once the foe has tackled again.
        Assert.Equal(2, result.Count<MoveMissed>());
        Assert.Contains(
            result.All<MoveUsed>(),
            m => m.AttackerName == "Player" && m.MoveName == "tackle"
        );
    }

    // ── Disable across a real battle ─────────────────────────────────────────

    private static int TurnOf<T>(RecordingEmitter log)
        where T : BattleEvent
    {
        int turn = 0;
        foreach (var e in log.Events)
        {
            if (e is TurnStarted)
                turn++;
            if (e is T)
                return turn;
        }
        return -1;
    }

    private (Creature Player, Creature Enemy) DisableDuel(int enemySpeed)
    {
        var player = TestCreatures.Make("Player", hp: 99999, speed: 100);
        player.AddAttack(Move("pound"));
        player.AddAttack(Move("tackle"));
        var enemy = TestCreatures.Make("Enemy", hp: 200, speed: enemySpeed);
        enemy.AddAttack(Move("disable"));
        return (player, enemy);
    }

    private static async Task<RecordingEmitter> RunDisableDuel(
        Creature player,
        Creature enemy,
        IBattleInput input,
        int disableTurns,
        Party? party = null,
        Items.Bag? bag = null
    )
    {
        var log = new RecordingEmitter();
        await new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("disable"),
            rules: new ScriptableRules().Deterministic().DisableTurns(disableTurns),
            emitter: log,
            rng: new LowestRollSource(),
            playerBag: bag,
            playerParty: party
        ).StartFightAsync();
        return log;
    }

    [Theory]
    [InlineData(200, 2)] // Disable lands first, so the target's attempt that turn counts
    [InlineData(1, 3)] // the target already moved, so the landing turn doesn't count
    public async Task ARollOfTwoCountsTheLandingTurnOnlyWhenTheTargetMovesAfterIt(
        int enemySpeed,
        int reEnabledOnTurn
    )
    {
        var (player, enemy) = DisableDuel(enemySpeed);

        var log = await RunDisableDuel(player, enemy, new ScriptedInput("tackle"), disableTurns: 2);

        Assert.Equal(reEnabledOnTurn, TurnOf<MoveReEnabled>(log));
    }

    [Fact]
    public async Task AnItemTurnDoesNotCountDisableDown()
    {
        var (player, enemy) = DisableDuel(enemySpeed: 200);
        player.Attributes.ReceiveDamage(50);
        var bag = new Items.Bag();
        bag.Add(17, 1);

        var log = await RunDisableDuel(
            player,
            enemy,
            new ItemOnSecondTurnInput(),
            disableTurns: 3,
            bag: bag
        );

        Assert.Single(log.Of<ItemUsed>());
        Assert.Equal(4, TurnOf<MoveReEnabled>(log));
    }

    private sealed class ItemOnSecondTurnInput : IBattleInput
    {
        private int _turn;

        private static PokemonAttack Tackle(TurnContext context) =>
            context.Attacker.MoveSet.First(m => m.Base.Name == "tackle");

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            Task.FromResult(Tackle(context));

        public Task<TurnChoice> ChooseTurnActionAsync(TurnContext context) =>
            Task.FromResult<TurnChoice>(
                ++_turn == 2
                    ? new ItemTurnChoice(
                        new Items.Item
                        {
                            Id = 17,
                            Name = "potion",
                            Category = Items.ItemCategory.Healing,
                            HealAmount = 20,
                        }
                    )
                    : new MoveTurnChoice(Tackle(context))
            );
    }

    [Fact]
    public async Task ASwitchTurnDoesNotCountTheOutgoingCreaturesDisableDown()
    {
        var (lead, enemy) = DisableDuel(enemySpeed: 200);
        var bench = TestCreatures.Make("Bench", hp: 99999, attack: 999, speed: 100);
        bench.AddAttack(Move("tackle"));
        var party = new Party(lead);
        party.Add(bench);

        await RunDisableDuel(
            lead,
            enemy,
            new ScriptedInput("tackle").TurnPlan(null, 1),
            disableTurns: 3,
            party: party
        );

        Assert.Equal(2, lead.Battle.DisableTurnsRemaining);
    }

    // ── What clears a recharge ───────────────────────────────────────────────

    private static ScriptableRules Forced() =>
        new ScriptableRules().Deterministic().ForceSecondary();

    private static Creature RechargingDefender()
    {
        var defender = TestCreatures.Make("D", hp: 99999, defense: 200, special: 200);
        defender.Battle.IsRecharging = true;
        return defender;
    }

    [Theory]
    [InlineData("bite")]
    [InlineData("ice-beam")]
    [InlineData("wrap")]
    public async Task AFlinchFreezeOrTrapClearsThePendingRecharge(string move)
    {
        var result = await new MoveScenario()
            .Rules(Forced())
            .Defender(RechargingDefender())
            .Use(Move(move));

        Assert.False(result.Defender.Battle.IsRecharging);
    }

    [Fact]
    public async Task ABurnLeavesThePendingRecharge()
    {
        var result = await new MoveScenario()
            .Rules(Forced())
            .Defender(RechargingDefender())
            .Use(Move("ember"));

        Assert.Equal(StatusCondition.Burn, result.Defender.Battle.Status);
        Assert.True(result.Defender.Battle.IsRecharging);
    }

    [Fact]
    public async Task ATrapMoveThatMissesStillClearsTheRecharge()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(RechargingDefender())
            .Use(Move("wrap"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Defender.Battle.IsRecharging);
        Assert.Equal(0, result.Defender.Battle.BindingTurnsRemaining);
    }

    [Fact]
    public async Task AContinuingTrapDoesNotClearARecharge()
    {
        var attacker = TestCreatures.Make("A", attack: 150);
        var defender = RechargingDefender();
        attacker.AddAttack(Move("wrap"));
        attacker.Battle.BindingMove = attacker.MoveSet[0];
        attacker.Battle.BindingTarget = defender;
        defender.Battle.BindingTurnsRemaining = 3;

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            Gen1TypeChart.Instance,
            AlwaysHitRules.Instance
        );
        await action.ExecuteAsync();

        Assert.True(defender.Battle.IsRecharging);
    }

    [Fact]
    public async Task AFlinchedRechargerLosesOneTurnNotTwo()
    {
        var result = await new MoveScenario()
            .Rules(Forced())
            .Defender(RechargingDefender())
            .Use(Move("bite"));

        var (first, log) = Check(result.Defender);
        var (second, _) = Check(result.Defender);

        Assert.False(first);
        Assert.Single(log.Of<FlinchBlocked>());
        Assert.Empty(log.Of<Recharging>());
        Assert.True(second);
    }

    // ── A sleep move on a recharging foe ─────────────────────────────────────

    [Fact]
    public async Task ASleepMoveOnARechargingFoeSkipsEveryCheckAndLandsOverAStatus()
    {
        var defender = RechargingDefender();
        defender.Battle.Status = StatusCondition.Poison;
        defender.Battle.SubstituteHp = 40;

        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(defender)
            .Use(Move("hypnosis"));

        Assert.Equal(StatusCondition.Sleep, defender.Battle.Status);
        Assert.True(defender.Battle.SleepTurns > 0);
        Assert.False(defender.Battle.IsRecharging);
        Assert.Single(result.Emitter.Of<StatusApplied>());
        Assert.False(result.Has<MoveMissed>());
    }

    [Fact]
    public async Task ASleepMoveOnAFoeThatIsNotRechargingStillRollsToHit()
    {
        var defender = TestCreatures.Make("D");

        await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(defender)
            .Use(Move("hypnosis"));

        Assert.Equal(StatusCondition.None, defender.Battle.Status);
    }

    // ── A replaced binder ────────────────────────────────────────────────────

    private async Task<RecordingEmitter> BinderFaintsToLeechSeed(IBattleRules rules)
    {
        var lead = TestCreatures.Make("Lead", hp: 160, attack: 50, speed: 1);
        lead.Attributes.HP = 1;
        lead.AddAttack(Move("wrap"));
        var bench = TestCreatures.Make("Bench", hp: 500, attack: 999, speed: 100);
        bench.AddAttack(Move("tackle"));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make("Foe", hp: 120, defense: 50, speed: 200);
        enemy.AddAttack(Move("leech-seed"));

        var recorder = new RecordingEmitter();
        await new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput("wrap", "tackle").PicksSwitchIn(1),
            new ScriptedInput("leech-seed"),
            rules: rules,
            emitter: recorder,
            rng: new HighestRollSource(),
            playerParty: party
        ).StartFightAsync();
        return recorder;
    }

    private static List<BattleEvent> AfterSendOut(RecordingEmitter recorder)
    {
        var events = recorder.Events.ToList();
        return events.Skip(events.FindIndex(e => e is CreatureSwitchedIn)).ToList();
    }

    [Fact]
    public async Task ReplacingTheBinderFreesTheFoeBeforeItsCounterRunsOut()
    {
        var recorder = await BinderFaintsToLeechSeed(
            new ScriptableRules().Deterministic().BindingTurns(5)
        );

        Assert.Single(recorder.Of<BindingStarted>());
        var after = AfterSendOut(recorder);
        Assert.DoesNotContain(after, e => e is BindingBlocked);
        Assert.Contains(after, e => e is MoveUsed m && m.AttackerName == "Foe");
    }

    [Fact]
    public async Task TheSeamDecidesWhetherReplacingTheBinderFreesTheFoe()
    {
        var recorder = await BinderFaintsToLeechSeed(new AltPreMoveRules());

        Assert.Contains(AfterSendOut(recorder), e => e is BindingBlocked);
    }

    // ── The seams ────────────────────────────────────────────────────────────

    [Fact]
    public void TheSeamSetsTheOrderOfTheChecks()
    {
        var creature = TestCreatures.Make("Mon");
        Lose(creature, PreMoveCheck.Recharge);
        Lose(creature, PreMoveCheck.Sleep);

        var (_, log) = Check(creature, rules: new AltPreMoveRules());

        Assert.Single(log.Of<Recharging>());
        Assert.Equal(3, creature.Battle.SleepTurns);
    }

    [Fact]
    public void TheSeamDecidesWhichLostTurnsEndALockIn()
    {
        var (binder, victim) = LockedIntoEverything();
        Lose(binder, PreMoveCheck.Sleep);

        Check(binder, rules: new AltPreMoveRules());

        Assert.Equal(0, binder.Battle.RampageTurnsRemaining);
        Assert.Equal(0, victim.Battle.BindingTurnsRemaining);
    }

    [Fact]
    public void TheSeamDecidesWhetherASleepTurnErasesTheLastMove()
    {
        var creature = TestCreatures.Make("Mon");
        creature.Battle.LastMoveUsed = Move("tackle");
        Lose(creature, PreMoveCheck.Sleep);

        Check(creature, rules: new AltPreMoveRules());

        Assert.NotNull(creature.Battle.LastMoveUsed);
    }

    [Fact]
    public void TheSeamDecidesWhetherADisabledMoveEndsTheCharge()
    {
        var creature = WithDisabledPound();
        creature.Battle.IsTwoTurnCharging = true;
        var order = new AltPreMoveRules { IncludeDisabledMoveCheck = true };

        Check(creature, selected: creature.MoveSet[0], rules: order);

        Assert.True(creature.Battle.IsTwoTurnCharging);
    }

    [Theory]
    [InlineData("bite")]
    [InlineData("ice-beam")]
    [InlineData("wrap")]
    public async Task TheSeamDecidesWhetherAnInflictionClearsTheRecharge(string move)
    {
        var result = await new MoveScenario()
            .Rules(new AltPreMoveRules())
            .Defender(RechargingDefender())
            .Use(Move(move));

        Assert.True(result.Defender.Battle.IsRecharging);
    }

    [Fact]
    public async Task TheSeamDecidesWhetherASleepMoveSkipsItsChecksOnARechargingFoe()
    {
        var defender = RechargingDefender();
        defender.Battle.Status = StatusCondition.Poison;

        await new MoveScenario()
            .Rules(new AltPreMoveRules())
            .Defender(defender)
            .Use(Move("hypnosis"));

        Assert.Equal(StatusCondition.Poison, defender.Battle.Status);
        Assert.True(defender.Battle.IsRecharging);
    }

    private sealed class AltPreMoveRules : DelegatingBattleRules
    {
        public bool IncludeDisabledMoveCheck { get; init; }

        public override IReadOnlyList<PreMoveCheck> PreMoveCheckOrder =>
            IncludeDisabledMoveCheck
                ? [PreMoveCheck.UsingDisabledMove]
                : [PreMoveCheck.Recharge, PreMoveCheck.Sleep, PreMoveCheck.HeldByTrap];

        public override bool PreMoveCheckCancelsLockIn(PreMoveCheck check) =>
            check == PreMoveCheck.Sleep;

        public override bool DisabledMoveBlockEndsCharge => false;

        public override bool InflictionClearsRecharge(RechargeClearingInfliction infliction) =>
            false;

        public override bool SleepMoveIgnoresChecksOnRechargingTarget => false;

        public override bool SleepOrFreezeTurnForgetsLastMove => false;

        public override bool ReplacementEndsTrap => false;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override int GetSecondaryEffectChance(Attack move, SecondaryEffectKind kind) => 100;

        public override int RollBindingTurns() => 5;

        public override double GetCritChance(Creature attacker, Attack move) => 0;
    }
}
