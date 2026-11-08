using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;
using static creaturegame.Tests.TestSupport.RecordingRandomSource;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>The order a move draws its rolls — IBattleRules.HitRollOrder and its two siblings; see
/// GENERATION_SEAMS.md §2. Rules and action share one recorded stream.</summary>
[Collection(MovesCollection.Name)]
public class RollOrderContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    private static Creature NoCritAttacker(int speed = 100) =>
        TestCreatures.Make("A", speed: speed, baseSpeed: 0);

    private static async Task<(MoveResult Result, IReadOnlyList<string> Draws)> UseGen1(
        Attack move,
        IRandomSource inner,
        Creature? attacker = null,
        Creature? defender = null
    )
    {
        var rng = new RecordingRandomSource(inner);
        var result = await new MoveScenario()
            .Rules(new Gen1BattleRules(rng))
            .Rng(rng)
            .Attacker(attacker ?? NoCritAttacker())
            .Defender(defender ?? TestCreatures.Make("D", hp: 5000))
            .Use(move);
        return (result, rng.Draws);
    }

    private static async Task<(MoveResult Result, IReadOnlyList<string> Draws)> UseAccuracyFirst(
        Attack move,
        IRandomSource inner,
        Creature? attacker = null,
        Creature? defender = null
    )
    {
        var rng = new RecordingRandomSource(inner);
        var result = await new MoveScenario()
            .Rules(new AccuracyFirstRules(rng))
            .Rng(rng)
            .Attacker(attacker ?? NoCritAttacker())
            .Defender(defender ?? TestCreatures.Make("D", hp: 5000))
            .Use(move);
        return (result, rng.Draws);
    }

    [Fact]
    public async Task ADamagingMoveRollsCritAndDamageBeforeAccuracy()
    {
        var (result, draws) = await UseGen1(Move("tackle"), new LowestRollSource());

        Assert.Equal([Crit, DamageRoll, Accuracy], draws);
        Assert.True(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task AMissStillDrawsTheCritAndDamageRolls()
    {
        var (result, draws) = await UseGen1(Move("tackle"), new HighestRollSource());

        Assert.Equal([Crit, DamageRoll, Accuracy], draws);
        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task AnImmuneTargetThatRollsAMissShowsNoEffectNotAMiss()
    {
        var (result, draws) = await UseGen1(
            Move("tackle"),
            new HighestRollSource(),
            defender: TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500)
        );

        Assert.Equal([Crit, Accuracy], draws); // 0 damage is never randomized
        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Has<MoveMissed>());
    }

    [Fact]
    public async Task DreamEaterOnAnAwakeTargetDrawsCritAndDamageButNotAccuracy()
    {
        var (result, draws) = await UseGen1(Move("dream-eater"), new LowestRollSource());

        Assert.Equal([Crit, DamageRoll], draws);
        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task SwiftSkipsOnlyTheAccuracyDraw()
    {
        var (result, draws) = await UseGen1(Move("swift"), new HighestRollSource());

        Assert.Equal([Crit, DamageRoll], draws);
        Assert.True(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task XAccuracySkipsOnlyTheAccuracyDraw()
    {
        var attacker = NoCritAttacker();
        attacker.Battle.UsingXAccuracy = true;

        var (result, draws) = await UseGen1(
            Move("tackle"),
            new HighestRollSource(),
            attacker: attacker
        );

        Assert.Equal([Crit, DamageRoll], draws);
        Assert.True(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task AMultiHitMoveRollsCountCritDamageAndAccuracyOnce()
    {
        var (result, draws) = await UseGen1(
            Move("fury-attack"),
            new LowestRollSource(),
            attacker: TestCreatures.Make("A") // base Speed 100, so the lowest roll crits
        );

        Assert.Equal([MultiHitCount, Crit, DamageRoll, Accuracy], draws);
        Assert.Equal(2, result.First<MultiHitCompleted>()!.Hits);
        Assert.All(result.Hits, h => Assert.True(h.IsCrit));
        Assert.Single(result.Hits.Select(h => h.Damage).Distinct());
    }

    [Fact]
    public async Task ARepeatedStrikeIsStillACritButOnlyTheFirstPrintsTheLine()
    {
        var (result, _) = await UseGen1(
            Move("fury-attack"),
            new LowestRollSource(),
            attacker: TestCreatures.Make("A")
        );

        Assert.All(result.Hits, h => Assert.True(h.IsCrit));
        Assert.Equal([true, false], result.Hits.Select(h => h.PrintsCritLine));
    }

    [Fact]
    public async Task EveryStrikePrintsItsEffectivenessLineInGen1()
    {
        var (result, _) = await UseGen1(Move("fury-attack"), new LowestRollSource());

        Assert.Equal(2, result.Hits.Count);
        Assert.All(result.Hits, h => Assert.True(h.PrintsEffectivenessLine));
    }

    private sealed class FirstStrikeEffectivenessRules : DelegatingBattleRules
    {
        public override int RollMultiHitCount() => 3;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override bool EffectivenessLineRepeatsOnEveryStrike => false;
    }

    [Fact]
    public async Task TheRulesCanLimitTheEffectivenessLineToTheFirstStrike()
    {
        var result = await new MoveScenario()
            .Rules(new FirstStrikeEffectivenessRules())
            .Defender(TestCreatures.Make("D", hp: 5000))
            .Use(Move("fury-attack"));

        Assert.Equal([true, false, false], result.Hits.Select(h => h.PrintsEffectivenessLine));
    }

    private sealed class EachStrikeRollsItsOwnCritRules : DelegatingBattleRules
    {
        public override bool MultiHitRepeatsFirstStrike => false;

        public override int RollMultiHitCount() => 3;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 1.0;
    }

    [Fact]
    public async Task AStrikeThatRollsItsOwnCritPrintsItsOwnLine()
    {
        var result = await new MoveScenario()
            .Rules(new EachStrikeRollsItsOwnCritRules())
            .Defender(TestCreatures.Make("D", hp: 5000))
            .Use(Move("fury-attack"));

        Assert.Equal(3, result.Hits.Count);
        Assert.All(result.Hits, h => Assert.True(h.PrintsCritLine));
    }

    [Fact]
    public async Task EveryStrikeRepeatsTheFirstStrikesDamage()
    {
        var rng = new RecordingRandomSource(new SeededRandomSource(7));
        var result = await new MoveScenario()
            .Rules(new FiveHitAlwaysHitRules(rng))
            .Rng(rng)
            .Defender(TestCreatures.Make("D", hp: 5000))
            .Use(Move("fury-attack"));

        Assert.Equal(5, result.Hits.Count);
        Assert.Single(result.Hits.Select(h => (h.Damage, h.IsCrit)).Distinct());
        Assert.Single(rng.Draws, d => d == DamageRoll);
    }

    [Fact]
    public async Task AFixedCountMultiHitMoveDrawsNoCount()
    {
        var (_, draws) = await UseGen1(Move("double-kick"), new LowestRollSource());

        Assert.Equal([Crit, DamageRoll, Accuracy], draws);
    }

    [Fact]
    public async Task ASlowerOhkoUserDrawsOnlyTheCritRoll()
    {
        var (result, draws) = await UseGen1(
            Move("fissure"),
            new LowestRollSource(),
            attacker: NoCritAttacker(speed: 50),
            defender: TestCreatures.Make("D", hp: 500, speed: 200)
        );

        Assert.Equal([Crit], draws);
        Assert.True(result.Has<MoveUnaffected>());
    }

    [Fact]
    public async Task AFasterOhkoUserDrawsCritDamageThenAccuracy()
    {
        var (result, draws) = await UseGen1(
            Move("fissure"),
            new LowestRollSource(),
            attacker: NoCritAttacker(speed: 200),
            defender: TestCreatures.Make("D", hp: 500, speed: 50)
        );

        Assert.Equal([Crit, DamageRoll, Accuracy], draws);
        Assert.False(result.Defender.IsAlive());
        Assert.False(result.First<DamageDealt>()!.IsCrit);
    }

    [Theory]
    [InlineData("sonic-boom")]
    [InlineData("seismic-toss")]
    [InlineData("super-fang")]
    public async Task DirectDamageDrawsOnlyAccuracy(string move)
    {
        var (result, draws) = await UseGen1(Move(move), new LowestRollSource());

        Assert.Equal([Accuracy], draws);
        Assert.True(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task PsywaveDrawsItsDamageAfterAccuracy()
    {
        var (result, draws) = await UseGen1(Move("psywave"), new LowestRollSource());

        Assert.Equal(2, draws.Count);
        Assert.Equal(Accuracy, draws[0]);
        Assert.StartsWith("Next(1,", draws[1]);
        Assert.Equal(1, result.TotalDamage);
    }

    [Fact]
    public async Task ThunderWaveOnAGroundTargetHasNoEffectWithoutAnyDraw()
    {
        var (result, draws) = await UseGen1(
            Move("thunder-wave"),
            new LowestRollSource(),
            defender: TestCreatures.Make("D", type1: DamageType.Ground, hp: 500)
        );

        Assert.Empty(draws);
        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ThunderWaveOnAGroundTargetRollsAccuracyFirstWhenTheRulesSaySo(bool rollMisses)
    {
        var rng = new RecordingRandomSource(
            rollMisses ? new HighestRollSource() : new LowestRollSource()
        );
        var result = await new MoveScenario()
            .Rules(new StatusImmunityAfterAccuracyRules(rng))
            .Rng(rng)
            .Defender(TestCreatures.Make("D", type1: DamageType.Ground, hp: 500))
            .Use(Move("thunder-wave"));

        Assert.Equal([Accuracy], rng.Draws);
        Assert.Equal(rollMisses, result.Has<MoveDidntAffect>());
        Assert.Equal(!rollMisses, result.Has<MoveHadNoEffect>());
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
    }

    private static Creature OneDamageAttacker() =>
        TestCreatures.Make("A", level: 1, attack: 1, baseSpeed: 0);

    private static Creature RockWall() =>
        TestCreatures.Make("D", type1: DamageType.Rock, hp: 500, defense: 999);

    [Fact]
    public async Task DamageOfOneDrawsNoDamageRoll()
    {
        var (result, draws) = await UseGen1(
            Move("tackle"),
            new LowestRollSource(),
            attacker: OneDamageAttacker(),
            defender: RockWall()
        );

        Assert.Equal([Crit, Accuracy], draws);
        Assert.Equal(1, result.TotalDamage);
    }

    [Fact]
    public async Task DamageOfOneDrawsTheDamageRollWhenTheRulesAlwaysRoll()
    {
        var rng = new RecordingRandomSource(new LowestRollSource());
        await new MoveScenario()
            .Rules(new AlwaysDamageRollRules(rng))
            .Rng(rng)
            .Attacker(OneDamageAttacker())
            .Defender(RockWall())
            .Use(Move("tackle"));

        Assert.Equal([Crit, DamageRoll, Accuracy], rng.Draws);
    }

    [Fact]
    public async Task ExplosionRollsCritAndDamageBeforeAccuracyAndFaintsTheUser()
    {
        var (result, draws) = await UseGen1(Move("explosion"), new LowestRollSource());

        Assert.Equal([Crit, DamageRoll, Accuracy], draws);
        Assert.True(result.Has<DamageDealt>());
        Assert.False(result.Attacker.IsAlive());
    }

    [Fact]
    public async Task AMissedExplosionStillDrawsCritAndDamageAndFaintsTheUser()
    {
        var (result, draws) = await UseGen1(Move("explosion"), new HighestRollSource());

        Assert.Equal([Crit, DamageRoll, Accuracy], draws);
        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.False(result.Attacker.IsAlive());
    }

    [Fact]
    public async Task AFasterOhkoOnAnImmuneTargetDrawsCritAndAccuracyButNoDamageRoll()
    {
        var (result, draws) = await UseGen1(
            Move("fissure"),
            new LowestRollSource(),
            attacker: NoCritAttacker(speed: 200),
            defender: TestCreatures.Make("D", type1: DamageType.Flying, hp: 500, speed: 50)
        );

        Assert.Equal([Crit, Accuracy], draws);
        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }

    [Fact]
    public async Task AFasterOhkoOnAnImmuneTargetDrawsTheDamageRollWhenTheRulesAlwaysRoll()
    {
        var rng = new RecordingRandomSource(new LowestRollSource());
        await new MoveScenario()
            .Rules(new AlwaysDamageRollRules(rng))
            .Rng(rng)
            .Attacker(NoCritAttacker(speed: 200))
            .Defender(TestCreatures.Make("D", type1: DamageType.Flying, hp: 500, speed: 50))
            .Use(Move("fissure"));

        Assert.Equal([Crit, DamageRoll, Accuracy], rng.Draws);
    }

    [Theory]
    [InlineData("sleep-powder", StatusCondition.Sleep, DamageType.Water)]
    [InlineData("sleep-powder", StatusCondition.Burn, DamageType.Water)]
    [InlineData("stun-spore", StatusCondition.Burn, DamageType.Water)]
    [InlineData("toxic", StatusCondition.None, DamageType.Poison)]
    public async Task ATargetTheMoveCannotAffectDrawsNoAccuracyRoll(
        string move,
        StatusCondition status,
        DamageType targetType
    )
    {
        var defender = TestCreatures.Make("D", type1: targetType, hp: 500);
        defender.Battle.Status = status;

        var (_, draws) = await UseGen1(Move(move), new LowestRollSource(), defender: defender);

        Assert.DoesNotContain(Accuracy, draws);
    }

    [Fact]
    public async Task AStatusedTargetDrawsNoAccuracyRoll()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 500);
        defender.Battle.Status = StatusCondition.Burn;

        var (result, draws) = await UseGen1(
            Move("toxic"),
            new LowestRollSource(),
            defender: defender
        );

        Assert.DoesNotContain(Accuracy, draws);
        Assert.True(result.Has<MoveDidntAffect>());
    }

    [Fact]
    public async Task ThunderWaveOnAParalyzedGroundTypeSaysDidntAffectWithNoRoll()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Ground, hp: 500);
        defender.Battle.Status = StatusCondition.Paralysis;

        var (result, draws) = await UseGen1(
            Move("thunder-wave"),
            new LowestRollSource(),
            defender: defender
        );

        Assert.DoesNotContain(Accuracy, draws);
        Assert.True(result.Has<MoveDidntAffect>());
        Assert.False(result.Has<MoveHadNoEffect>());
    }

    [Fact]
    public async Task ThunderWaveOnAnUnstatusedGroundTypeSaysDoesntAffect()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Ground, hp: 500);

        var (result, draws) = await UseGen1(
            Move("thunder-wave"),
            new LowestRollSource(),
            defender: defender
        );

        Assert.DoesNotContain(Accuracy, draws);
        Assert.True(result.Has<MoveHadNoEffect>());
    }

    [Theory]
    [InlineData("growl")]
    [InlineData("confuse-ray")]
    public async Task AMoveASubstituteStopsDrawsNoAccuracyRoll(string move)
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 500);
        defender.Battle.SubstituteHp = 51;

        var (result, draws) = await UseGen1(Move(move), new LowestRollSource(), defender: defender);

        Assert.DoesNotContain(Accuracy, draws);
        Assert.True(result.Has<MoveFailed>());
    }

    [Fact]
    public async Task MistStopsAStatDownMoveWithNoAccuracyRoll()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 500);
        defender.Battle.HasMist = true;

        var (result, draws) = await UseGen1(
            Move("growl"),
            new LowestRollSource(),
            defender: defender
        );

        Assert.DoesNotContain(Accuracy, draws);
        Assert.True(result.Has<MoveFailed>());
    }

    private sealed class HitTestFirstRules(IRandomSource rng) : DelegatingBattleRules(rng)
    {
        public override bool PrimaryEffectChecksPrecedeHitTest => false;
    }

    private static (MoveScenario Scenario, RecordingRandomSource Rng) HitTestFirst(
        Creature defender
    )
    {
        var rng = new RecordingRandomSource(new LowestRollSource());
        var scenario = new MoveScenario()
            .Rules(new HitTestFirstRules(rng))
            .Rng(rng)
            .Defender(defender);
        return (scenario, rng);
    }

    [Fact]
    public async Task WithTheHitTestFirstAStatusedTargetStillGetsItsLine()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 500);
        defender.Battle.Status = StatusCondition.Burn;
        var (scenario, rng) = HitTestFirst(defender);

        var result = await scenario.Use(Move("toxic"));

        Assert.Contains(Accuracy, rng.Draws);
        Assert.True(result.Has<MoveDidntAffect>());
        Assert.Equal(StatusCondition.Burn, result.Defender.Battle.Status);
    }

    [Fact]
    public async Task WithTheHitTestFirstMistStillStopsAPrimaryDrop()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 500);
        defender.Battle.HasMist = true;
        var (scenario, rng) = HitTestFirst(defender);

        var result = await scenario.Use(Move("growl"));

        Assert.Contains(Accuracy, rng.Draws);
        Assert.True(result.Has<MoveFailed>());
        Assert.Equal(0, result.Defender.Battle.Stages.Attack);
    }

    [Theory]
    [InlineData("toxic", StatusCondition.None)]
    [InlineData("thunder-wave", StatusCondition.Paralysis)]
    public async Task WithTheHitTestFirstTheSubstituteRuleStillDecides(
        string move,
        StatusCondition expected
    )
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 500);
        defender.Battle.SubstituteHp = 51;
        var (scenario, _) = HitTestFirst(defender);

        var result = await scenario.Use(Move(move));

        Assert.Equal(expected, result.Defender.Battle.Status);
    }

    private sealed class MissedLineRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 0;

        public override MoveFailureAnnouncement PrimaryEffectFailureAnnouncement =>
            MoveFailureAnnouncement.Missed;
    }

    [Fact]
    public async Task TheRulesPickTheLineForAMissedStatDownMove()
    {
        var result = await new MoveScenario()
            .Rules(new MissedLineRules())
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move("growl"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<MoveFailed>());
    }

    [Fact]
    public async Task TheRulesCanPutTheHitTestFirst()
    {
        var rng = new RecordingRandomSource(new LowestRollSource());
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 500);
        defender.Battle.Status = StatusCondition.Burn;

        await new MoveScenario()
            .Rules(new HitTestFirstRules(rng))
            .Rng(rng)
            .Defender(defender)
            .Use(Move("toxic"));

        Assert.Contains(Accuracy, rng.Draws);
    }

    [Fact]
    public async Task AFireMoveThatFaintsAFrozenTargetDoesNotThawIt()
    {
        var defender = TestCreatures.Make("D", hp: 1);
        defender.Battle.Status = StatusCondition.Freeze;

        var (result, _) = await UseGen1(Move("ember"), new LowestRollSource(), defender: defender);

        Assert.False(result.Defender.IsAlive());
        Assert.False(result.Has<StatusCleared>());
    }

    [Fact]
    public async Task StruggleRollsCritAndDamageWithNoAccuracyDrawMirroringTheKnownGap()
    {
        var rng = new RecordingRandomSource(new LowestRollSource());
        var emitter = new RecordingEmitter();
        var defender = TestCreatures.Make("D", hp: 5000);
        await new AttackAction(
            NoCritAttacker(),
            defender,
            null,
            Gen1TypeChart.Instance,
            new Gen1BattleRules(rng),
            emitter,
            rng: rng
        ).ExecuteAsync();

        Assert.Equal([Crit, DamageRoll], rng.Draws);
        Assert.Contains(emitter.Events, e => e is DamageDealt);
    }

    [Fact]
    public async Task AccuracyFirstRulesFailDreamEaterOnAnAwakeTargetWithoutAnyDraw()
    {
        var (result, draws) = await UseAccuracyFirst(Move("dream-eater"), new LowestRollSource());

        Assert.Empty(draws);
        Assert.True(result.Has<MoveMissed>());
    }

    [Fact]
    public async Task CounterDrawsCritThenAccuracyWhenItQualifies()
    {
        var attacker = NoCritAttacker();
        attacker.Battle.LastDamageTaken = 30;
        attacker.Battle.LastDamageType = DamageType.Normal;

        var (result, draws) = await UseGen1(
            Move("counter"),
            new LowestRollSource(),
            attacker: attacker
        );

        Assert.Equal([Crit, Accuracy], draws);
        Assert.Equal(60, result.TotalDamage);
    }

    [Fact]
    public async Task CounterThatDoesNotQualifyStopsBeforeAccuracy()
    {
        var (result, draws) = await UseGen1(Move("counter"), new LowestRollSource());

        Assert.Equal([Crit], draws);
        Assert.True(result.Has<MoveMissed>());
    }

    [Fact]
    public async Task AStatusMoveDrawsNoCritOrDamageRoll()
    {
        var (_, draws) = await UseGen1(Move("growl"), new LowestRollSource());

        Assert.Equal(Accuracy, draws[0]);
        Assert.DoesNotContain(Crit, draws);
        Assert.DoesNotContain(DamageRoll, draws);
    }

    [Fact]
    public async Task ExplosionOnAnImmuneTargetHasNoEffectAndStillFaintsTheUser()
    {
        var (result, draws) = await UseGen1(
            Move("explosion"),
            new HighestRollSource(),
            defender: TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500)
        );

        Assert.Equal([Crit, Accuracy], draws);
        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Has<MoveMissed>());
        Assert.False(result.Attacker.IsAlive());
    }

    [Fact]
    public async Task AFireMoveThawsTheTargetAfterItsDamage()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 5000);
        defender.Battle.Status = StatusCondition.Freeze;

        var (result, _) = await UseGen1(Move("ember"), new LowestRollSource(), defender: defender);

        int damageAt = result.Events.ToList().FindIndex(e => e is DamageDealt);
        int thawAt = result.Events.ToList().FindIndex(e => e is StatusCleared);
        Assert.InRange(damageAt, 0, thawAt - 1);
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
    }

    [Fact]
    public async Task AccuracyFirstRulesDrawAccuracyBeforeCritAndDamage()
    {
        var (_, draws) = await UseAccuracyFirst(Move("tackle"), new LowestRollSource());

        Assert.Equal([Accuracy, Crit, DamageRoll], draws);
    }

    [Fact]
    public async Task AccuracyFirstRulesStopAMissBeforeAnyCritOrDamageRoll()
    {
        var (result, draws) = await UseAccuracyFirst(Move("tackle"), new HighestRollSource());

        Assert.Equal([Accuracy], draws);
        Assert.True(result.Has<MoveMissed>());
    }

    [Fact]
    public async Task AccuracyFirstRulesRollEachMultiHitStrikeSeparately()
    {
        var (_, draws) = await UseAccuracyFirst(Move("fury-attack"), new LowestRollSource());

        Assert.Equal([Accuracy, MultiHitCount, Crit, DamageRoll, Crit, DamageRoll], draws);
    }

    [Fact]
    public async Task AccuracyFirstRulesShowAMissOnAnImmuneTargetThatMisses()
    {
        var (result, _) = await UseAccuracyFirst(
            Move("tackle"),
            new HighestRollSource(),
            defender: TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500)
        );

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<MoveHadNoEffect>());
    }

    [Fact]
    public async Task AccuracyFirstRulesCheckTheOhkoSpeedBeforeAnyDraw()
    {
        var (slower, slowerDraws) = await UseAccuracyFirst(
            Move("fissure"),
            new LowestRollSource(),
            attacker: NoCritAttacker(speed: 50),
            defender: TestCreatures.Make("D", hp: 500, speed: 200)
        );
        var (_, fasterDraws) = await UseAccuracyFirst(
            Move("fissure"),
            new LowestRollSource(),
            attacker: NoCritAttacker(speed: 200),
            defender: TestCreatures.Make("D", hp: 500, speed: 50)
        );

        Assert.Empty(slowerDraws);
        Assert.True(slower.Has<MoveUnaffected>());
        Assert.Equal([Accuracy], fasterDraws);
    }

    private sealed class StatusImmunityAfterAccuracyRules(IRandomSource rng)
        : DelegatingBattleRules(rng)
    {
        public override bool StatusMoveImmunityBeforeAccuracy => false;
    }

    private sealed class AlwaysDamageRollRules(IRandomSource rng) : DelegatingBattleRules(rng)
    {
        public override bool DamageRollSkippedBelowTwo => false;
    }

    private sealed class FiveHitAlwaysHitRules(IRandomSource rng) : DelegatingBattleRules(rng)
    {
        public override int RollMultiHitCount() => 5;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;
    }
}
