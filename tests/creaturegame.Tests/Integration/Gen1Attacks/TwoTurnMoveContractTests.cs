using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;
using static creaturegame.Tests.TestSupport.RecordingRandomSource;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>Two-turn charge moves, and Fly and Dig's charge turn out of reach (GENERATION_SEAMS.md §2).</summary>
[Collection(MovesCollection.Name)]
public class TwoTurnMoveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("razor-wind")]
    [InlineData("fly")]
    [InlineData("solar-beam")]
    [InlineData("dig")]
    [InlineData("skull-bash")]
    [InlineData("sky-attack")]
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
    [InlineData("razor-wind")]
    [InlineData("fly")]
    [InlineData("solar-beam")]
    [InlineData("dig")]
    [InlineData("skull-bash")]
    [InlineData("sky-attack")]
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
        player.Attributes.Speed = 200; // outspeed the enemy every turn
        player.AddAttack(Move("fly"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
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
        var input = new CountingInput(0);
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            input,
            AutoSelectInput.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        Assert.Contains(emitter.Events, e => e is ChargingUp);
        Assert.Contains(emitter.Events, e => e is DamageDealt d && d.TargetName == "Enemy");
        Assert.False(enemy.IsAlive());
        Assert.Equal(1, input.CallCount); // asked only on the charge turn
    }

    // ── Fly and Dig: out of reach on the charge turn ─────────────────────────

    private static Task Act(
        Creature user,
        Creature foe,
        int slot = 0,
        IBattleRules? rules = null
    ) =>
        new AttackAction(
            user,
            foe,
            user.MoveSet[slot],
            Gen1TypeChart.Instance,
            rules ?? AlwaysHitRules.Instance
        ).ExecuteAsync();

    private async Task<Creature> Charged(
        string move,
        IBattleRules? rules = null,
        DamageType type1 = DamageType.Normal
    )
    {
        var user = TestCreatures.Make("Flyer", type1: type1, hp: 500);
        user.AddAttack(Move(move));
        user.AddAttack(Move("solar-beam"));
        await Act(user, TestCreatures.Make("Foe"), rules: rules);
        return user;
    }

    private static async Task<(MoveResult Result, IReadOnlyList<string> Draws)> Strike(
        Attack move,
        Creature target,
        Creature? attacker = null,
        IBattleRules? rules = null
    )
    {
        var rng = new RecordingRandomSource(new LowestRollSource());
        var result = await new MoveScenario()
            .Rules(rules ?? new Gen1BattleRules(rng))
            .Rng(rng)
            .Attacker(attacker ?? TestCreatures.Make("Foe", baseSpeed: 0))
            .Defender(target)
            .Use(move);
        return (result, rng.Draws);
    }

    [Theory]
    [InlineData("fly", SemiInvulnerability.Flying)]
    [InlineData("dig", SemiInvulnerability.Underground)]
    public async Task AHitOnAFlyOrDigChargeTurnMissesWithoutAnAccuracyRoll(
        string move,
        SemiInvulnerability away
    )
    {
        var user = await Charged(move);

        var (result, draws) = await Strike(Move("tackle"), user);

        Assert.Equal(away, user.Battle.SemiInvulnerability);
        Assert.Equal([Crit, DamageRoll], draws);
        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, user.Attributes.HP);
    }

    [Theory]
    [InlineData("razor-wind")]
    [InlineData("solar-beam")]
    [InlineData("skull-bash")]
    [InlineData("sky-attack")]
    public async Task OtherChargeTurnsStayWithinReach(string move)
    {
        var user = await Charged(move);

        var (result, draws) = await Strike(Move("tackle"), user);

        Assert.True(user.Battle.IsTwoTurnCharging);
        Assert.Equal(SemiInvulnerability.None, user.Battle.SemiInvulnerability);
        Assert.Equal([Crit, DamageRoll, Accuracy], draws);
        Assert.True(result.Has<DamageDealt>());
    }

    [Theory]
    [InlineData("fly")]
    [InlineData("dig")]
    public async Task SwiftHitsAFlyOrDigUser(string move)
    {
        var user = await Charged(move);

        var (result, draws) = await Strike(Move("swift"), user);

        Assert.Equal([Crit, DamageRoll], draws);
        Assert.True(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task XAccuracyDoesNotGetThroughToAFlyer()
    {
        var user = await Charged("fly");
        var attacker = TestCreatures.Make("Foe", baseSpeed: 0);
        attacker.Battle.UsingXAccuracy = true;

        var (result, draws) = await Strike(Move("tackle"), user, attacker);

        Assert.Equal([Crit, DamageRoll], draws);
        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task StruggleMissesAFlyer()
    {
        var user = await Charged("fly");
        var attacker = TestCreatures.Make("Foe", baseSpeed: 0);
        var emitter = new RecordingEmitter();

        await new AttackAction(
            attacker,
            user,
            null,
            Gen1TypeChart.Instance,
            AlwaysHitRules.Instance,
            emitter,
            rng: new LowestRollSource()
        ).ExecuteAsync();

        Assert.NotEmpty(emitter.Of<MoveMissed>());
        Assert.Empty(emitter.Of<DamageDealt>());
        Assert.Empty(emitter.Of<RecoilDamage>());
    }

    [Fact]
    public async Task ATypeImmuneFlyerPrintsNoEffectInsteadOfAMiss()
    {
        var user = await Charged("fly", type1: DamageType.Flying);

        var (result, draws) = await Strike(Move("earthquake"), user);

        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Has<MoveMissed>());
        Assert.DoesNotContain(Accuracy, draws);
        Assert.Equal(500, user.Attributes.HP);
    }

    [Fact]
    public async Task AOneHitKoFromAFasterUserMissesADigger()
    {
        var user = await Charged("dig");
        var attacker = TestCreatures.Make("Foe", speed: 999);

        var (result, draws) = await Strike(Move("horn-drill"), user, attacker);

        Assert.True(result.Has<MoveMissed>());
        Assert.DoesNotContain(Accuracy, draws);
        Assert.Equal(500, user.Attributes.HP);
    }

    [Fact]
    public async Task SelfDestructOnAFlyerStillFaintsItsUser()
    {
        var user = await Charged("fly");

        var (result, _) = await Strike(Move("self-destruct"), user);

        Assert.False(result.Attacker.IsAlive());
        Assert.Equal(500, user.Attributes.HP);
    }

    [Fact]
    public async Task JumpKickOnAFlyerStillCrashes()
    {
        var user = await Charged("fly");

        var (result, _) = await Strike(Move("jump-kick"), user);

        Assert.True(result.Has<CrashDamage>());
        Assert.Equal(500, user.Attributes.HP);
    }

    [Fact]
    public async Task AThrashThatMissesAFlyerStillStartsTheRampage()
    {
        var user = await Charged("fly");

        var (result, _) = await Strike(Move("thrash"), user);

        Assert.True(result.Has<MoveMissed>());
        Assert.NotNull(result.Attacker.Battle.RampageMove);
    }

    [Fact]
    public async Task ARampageThatFailsOnAFlyerStillEndsItsLock()
    {
        var user = await Charged("fly");

        var (result, _) = await Strike(Move("thrash"), user, rules: new OneTurnRampageFails());

        Assert.True(result.Has<MoveFailed>());
        Assert.Null(result.Attacker.Battle.RampageMove);
        Assert.True(result.Attacker.Battle.ConfusedTurns > 0);
    }

    private sealed class OneTurnRampageFails : DelegatingBattleRules
    {
        public override int RollRampageTurns() => 1;

        public override SemiInvulnerableHit HitOnSemiInvulnerable(
            Attack move,
            SemiInvulnerability state
        ) => SemiInvulnerableHit.Fails;
    }

    [Fact]
    public async Task UnderAccuracyFirstOrderAFlyerIsMissedWithNoDrawAtAll()
    {
        var user = await Charged("fly");

        var (result, draws) = await Strike(
            Move("tackle"),
            user,
            rules: new AccuracyFirstRules(new LowestRollSource())
        );

        Assert.True(result.Has<MoveMissed>());
        Assert.Empty(draws);
        Assert.Equal(500, user.Attributes.HP);
    }

    [Theory]
    [InlineData("thunder-wave", typeof(MoveDidntAffect))]
    [InlineData("hypnosis", typeof(MoveDidntAffect))]
    [InlineData("growl", typeof(MoveFailed))]
    [InlineData("supersonic", typeof(MoveFailed))]
    [InlineData("disable", typeof(MoveFailed))]
    public async Task AStatusMoveOnAFlyerPrintsItsUsualMissLineWithoutARoll(
        string move,
        Type missLine
    )
    {
        var user = await Charged("fly");

        var (result, draws) = await Strike(Move(move), user);

        Assert.Empty(draws);
        Assert.Contains(result.Events, e => e.GetType() == missLine);
        Assert.Equal(StatusCondition.None, user.Battle.Status);
        Assert.Equal(0, user.Battle.Stages.Attack);
        Assert.Equal(0, user.Battle.ConfusedTurns);
        Assert.Null(user.Battle.DisabledMove);
    }

    [Theory]
    [InlineData("roar")]
    [InlineData("whirlwind")]
    public async Task RoarAndWhirlwindStillDriveOffAFlyer(string move)
    {
        var user = await Charged("fly");

        var (result, _) = await Strike(Move(move), user);

        Assert.True(user.Battle.HasFled);
        Assert.False(result.Has<MoveMissed>());
    }

    [Fact]
    public async Task TransformStillCopiesAFlyer()
    {
        var user = await Charged("fly", type1: DamageType.Flying);

        var (result, _) = await Strike(Move("transform"), user);

        Assert.True(result.Has<TransformedInto>());
        Assert.Equal(DamageType.Flying, result.Attacker.Type1);
    }

    [Fact]
    public async Task HazeLandsOnAFlyerButLeavesItOutOfReach()
    {
        var user = await Charged("fly");
        user.Battle.Stages.RaiseAttack(2);

        var (haze, _) = await Strike(Move("haze"), user);
        var (tackle, _) = await Strike(Move("tackle"), user);

        Assert.True(haze.Has<HazeClearedStages>());
        Assert.Equal(0, user.Battle.Stages.Attack);
        Assert.Equal(SemiInvulnerability.Flying, user.Battle.SemiInvulnerability);
        Assert.True(tackle.Has<MoveMissed>());
    }

    [Theory]
    [InlineData("mimic")]
    [InlineData("conversion")]
    public async Task MimicAndConversionFailAgainstAFlyer(string move)
    {
        var user = await Charged("fly", type1: DamageType.Flying);

        var (result, draws) = await Strike(Move(move), user);

        Assert.Empty(draws);
        Assert.True(result.Has<MoveFailed>());
        Assert.False(result.Has<MimicLearned>());
        Assert.False(result.Has<ConvertedType>());
        Assert.Null(result.Attacker.Battle.MimicWrapper);
        Assert.Equal(DamageType.Normal, result.Attacker.Type1);
    }

    [Fact]
    public async Task SwordsDanceWorksWhileTheFoeIsAway()
    {
        var user = await Charged("fly");

        var (result, _) = await Strike(Move("swords-dance"), user);

        Assert.Equal(2, result.Attacker.Battle.Stages.Attack);
        Assert.False(result.Has<MoveMissed>());
    }

    [Fact]
    public async Task RecoverWorksWhileTheFoeIsAway()
    {
        var user = await Charged("fly");
        var attacker = TestCreatures.Make("Foe", hp: 200, baseSpeed: 0);
        attacker.Attributes.ReceiveDamage(100);

        var (result, _) = await Strike(Move("recover"), user, attacker);

        Assert.True(result.Has<Healed>());
        Assert.Equal(200, attacker.Attributes.HP);
    }

    [Theory]
    [InlineData("fly")]
    [InlineData("dig")]
    public async Task TheReleaseTurnBringsTheUserBackWithinReach(string move)
    {
        var user = await Charged(move);

        await Act(user, TestCreatures.Make("Foe"));
        var (result, _) = await Strike(Move("tackle"), user);

        Assert.Equal(SemiInvulnerability.None, user.Battle.SemiInvulnerability);
        Assert.True(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task AConfusionSelfHitBringsTheFlyerBackWithinReach()
    {
        var user = await Charged("fly");
        user.Battle.ConfusedTurns = 3;

        var log = new RecordingEmitter();
        bool acted = StatusResolver.CanAct(
            user,
            Gen1BattleRules.Instance,
            log,
            new LowestRollSource(),
            TestCreatures.Make("Foe"),
            user.MoveSet[0]
        );
        var (result, _) = await Strike(Move("tackle"), user);

        Assert.False(acted);
        Assert.NotEmpty(log.Of<ConfusionDamage>());
        Assert.Equal(SemiInvulnerability.None, user.Battle.SemiInvulnerability);
        Assert.True(result.Has<DamageDealt>());
    }

    [Theory]
    [InlineData(PreMoveCheck.Paralysis)]
    [InlineData(PreMoveCheck.UsingDisabledMove)]
    public async Task AFlyerThatLosesItsReleaseToParalysisOrIsDisabledStaysOutOfReachUntilItsNextRelease(
        PreMoveCheck lostTo
    )
    {
        var user = await Charged("fly");
        if (lostTo == PreMoveCheck.Paralysis)
            user.Battle.Status = StatusCondition.Paralysis;
        else
        {
            user.Battle.DisabledMove = user.MoveSet[0];
            user.Battle.DisableTurnsRemaining = 5;
        }
        var foe = TestCreatures.Make("Foe");

        bool acted = StatusResolver.CanAct(
            user,
            Gen1BattleRules.Instance,
            new RecordingEmitter(),
            new LowestRollSource(),
            foe,
            user.MoveSet[0]
        );
        var (stillAway, _) = await Strike(Move("tackle"), user);
        await Act(user, foe, slot: 1);
        var (duringSolarBeam, _) = await Strike(Move("tackle"), user);
        await Act(user, foe, slot: 1);
        var (afterRelease, _) = await Strike(Move("tackle"), user);

        Assert.False(acted);
        Assert.True(stillAway.Has<MoveMissed>());
        Assert.True(duringSolarBeam.Has<MoveMissed>());
        Assert.True(afterRelease.Has<DamageDealt>());
        Assert.Equal(SemiInvulnerability.None, user.Battle.SemiInvulnerability);
    }

    private static int TurnOf(BattleScenarioResult result, Func<BattleEvent, bool> match)
    {
        int turn = 0;
        foreach (var e in result.Events)
        {
            if (e is TurnStarted)
                turn++;
            if (match(e))
                return turn;
        }
        return -1;
    }

    private sealed class FullyParalyzedOnAttempt(int attempt) : DelegatingBattleRules
    {
        private int _attempts;

        public override bool RollsFullParalysis(IRandomSource rng) => ++_attempts == attempt;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature attacker, Attack move) => 0;

        public override double RollDamageVariance() => 1.0;
    }

    [Fact]
    public async Task AFullyParalyzedFlyerDodgesEveryHitUntilItCompletesALaterFly()
    {
        var player = TestCreatures.Make("Player", hp: 1, speed: 1);
        player.AddAttack(Move("fly"));
        var enemy = TestCreatures.Make("Enemy", hp: 500, speed: 200);
        enemy.AddAttack(Move("thunder-wave"));
        enemy.AddAttack(Move("tackle"));

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("fly")
            .EnemyUses("thunder-wave", "tackle")
            .Rules(new FullyParalyzedOnAttempt(2))
            .RunAsync();

        // Charge on turn 1, paralyzed on its release turn 2, a second Fly on turns 3 and 4, hit on turn 5.
        Assert.Equal(2, TurnOf(result, e => e is ActionBlocked { CreatureName: "Player" }));
        Assert.Equal(
            3,
            result.All<MoveMissed>().Count(m => m.AttackerName == "Enemy" && m.MoveName == "tackle")
        );
        Assert.Equal(4, TurnOf(result, e => e is DamageDealt { TargetName: "Enemy" }));
        Assert.Equal(5, TurnOf(result, e => e is DamageDealt { TargetName: "Player" }));
    }

    [Fact]
    public async Task SwitchingOutBringsAStrandedFlyerBackWithinReach()
    {
        var lead = TestCreatures.Make("Lead", hp: 1, speed: 1);
        lead.AddAttack(Move("fly"));
        var bench = TestCreatures.Make("Bench", hp: 9999, attack: 999, speed: 1);
        bench.AddAttack(Move("fly"));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make("Enemy", hp: 30, speed: 200);
        enemy.AddAttack(Move("thunder-wave"));
        enemy.AddAttack(Move("tackle"));

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(enemy)
            .PlayerUses("fly")
            .PlayerTurnPlan(null, 1, 0)
            .EnemyUses("thunder-wave", "tackle")
            .Rules(new FullyParalyzedOnAttempt(2))
            .RunAsync();

        // Stranded on turn 2, benched on turn 3, back on turn 4 and hit at once.
        Assert.Equal(2, TurnOf(result, e => e is ActionBlocked { CreatureName: "Lead" }));
        Assert.Equal(2, TurnOf(result, e => e is MoveMissed { AttackerName: "Enemy" }));
        Assert.Equal(4, TurnOf(result, e => e is DamageDealt { TargetName: "Lead" }));
        Assert.False(lead.IsAlive());
    }

    [Fact]
    public async Task BidesReleaseHitsAFoeThatHasFlownUp()
    {
        var player = TestCreatures.Make("Player", hp: 9999, speed: 1);
        player.AddAttack(Move("bide"));
        var enemy = TestCreatures.Make("Enemy", hp: 1, speed: 200);
        enemy.AddAttack(Move("tackle"));
        enemy.AddAttack(Move("fly"));

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("bide")
            .EnemyUses("tackle", "tackle", "fly")
            .Rules(new ScriptableRules().Deterministic().BideTurns(3))
            .RunAsync();

        Assert.Equal(3, TurnOf(result, e => e is ChargingUp { CreatureName: "Enemy" }));
        Assert.Equal(3, TurnOf(result, e => e is DamageDealt { TargetName: "Enemy" }));
        Assert.Equal(SemiInvulnerability.Flying, enemy.Battle.SemiInvulnerability);
        Assert.False(enemy.IsAlive());
    }

    // ── The seams ────────────────────────────────────────────────────────────

    private sealed class ReachRules : DelegatingBattleRules
    {
        public SemiInvulnerability? Grants { get; init; }
        public SemiInvulnerableHit? Hit { get; init; }
        public bool? LostTurnEnds { get; init; }

        public override SemiInvulnerability ChargeGrantsSemiInvulnerability(Attack move) =>
            Grants ?? base.ChargeGrantsSemiInvulnerability(move);

        public override SemiInvulnerableHit HitOnSemiInvulnerable(
            Attack move,
            SemiInvulnerability state
        ) => Hit ?? base.HitOnSemiInvulnerable(move, state);

        public override bool LostTurnEndsSemiInvulnerability(PreMoveCheck check) =>
            LostTurnEnds ?? base.LostTurnEndsSemiInvulnerability(check);

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;
    }

    [Fact]
    public async Task TheSeamDecidesWhichChargeTurnsGoOutOfReach()
    {
        var user = await Charged(
            "solar-beam",
            new ReachRules { Grants = SemiInvulnerability.Underground }
        );

        var (result, _) = await Strike(Move("tackle"), user);

        Assert.Equal(SemiInvulnerability.Underground, user.Battle.SemiInvulnerability);
        Assert.True(result.Has<MoveMissed>());
    }

    [Theory]
    [InlineData(SemiInvulnerableHit.Lands, typeof(DamageDealt))]
    [InlineData(SemiInvulnerableHit.Fails, typeof(MoveFailed))]
    public async Task TheSeamDecidesWhatAMoveDoesToATargetOutOfReach(
        SemiInvulnerableHit hit,
        Type outcome
    )
    {
        var user = await Charged("fly");

        var (result, _) = await Strike(Move("tackle"), user, rules: new ReachRules { Hit = hit });

        Assert.Contains(result.Events, e => e.GetType() == outcome);
        Assert.False(result.Has<MoveMissed>());
    }

    [Fact]
    public async Task TheSeamDecidesWhichLostTurnsBringTheUserBackWithinReach()
    {
        var user = await Charged("fly");
        user.Battle.Status = StatusCondition.Paralysis;

        StatusResolver.CanAct(
            user,
            new ReachRules { LostTurnEnds = true },
            null,
            new LowestRollSource(),
            TestCreatures.Make("Foe"),
            user.MoveSet[0]
        );

        Assert.Equal(SemiInvulnerability.None, user.Battle.SemiInvulnerability);
    }

    [Fact]
    public async Task UnderTheAltProfileSolarBeamGoesUndergroundAndATackleStillHits()
    {
        var rules = TestAltProfile.Instance.BattleRules;
        var user = await Charged("solar-beam", rules);

        var (result, _) = await Strike(Move("tackle"), user, rules: rules);

        Assert.Equal(SemiInvulnerability.Underground, user.Battle.SemiInvulnerability);
        Assert.True(result.Has<DamageDealt>());
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
