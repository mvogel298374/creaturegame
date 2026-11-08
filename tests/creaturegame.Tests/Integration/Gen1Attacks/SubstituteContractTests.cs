using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Substitute (Gen 1): the user spends floor(maxHP/4) HP to raise a decoy with floor(maxHP/4)+1 HP.
/// While it stands, the foe's hits strike the decoy (the user takes nothing, and overflow damage is
/// lost), and the foe's poison, stat drops and confusion are shielded. It fails if a Substitute is
/// already up or the user can't pay the HP cost. The decoy persists across turns, so the last test
/// drives a full <see cref="Battle"/> to prove it soaks real enemy attacks on later turns and protects
/// the user's HP until it breaks.
/// </summary>
[Collection(MovesCollection.Name)]
public class SubstituteContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task PutsUpADecoyForAQuarterOfMaxHp()
    {
        var user = TestCreatures.Make("A", hp: 200);

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        int cost = 200 / 4; // 50
        Assert.True(result.Has<SubstituteCreated>());
        Assert.Equal(cost + 1, result.Attacker.Battle.SubstituteHp); // decoy has the cost +1 (Gen 1)
        Assert.Equal(200 - cost, result.Attacker.Attributes.HP); // user paid exactly the cost
        Assert.False(result.Has<DamageDealt>(), "Substitute deals no damage to the foe");
    }

    [Fact]
    public async Task DecoyAbsorbsAHitAndTheUserTakesNothing()
    {
        var defender = TestCreatures.Make("D", hp: 300, defense: 100);
        defender.Battle.SubstituteHp = 200; // plenty to survive one weak hit
        var attacker = TestCreatures.Make("A", attack: 80);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("tackle"));

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<SubstituteBroke>(), "the decoy survived, so no break line");
        Assert.False(result.Has<DamageDealt>(), "the user behind the decoy takes no damage");
        Assert.Equal(300, result.Defender.Attributes.HP); // HP untouched
        Assert.InRange(result.Defender.Battle.SubstituteHp, 1, 199); // decoy soaked the hit but didn't break
    }

    // The decoy soaks every damage category, not just the Standard path — Seismic Toss (level-based,
    // bypasses the normal damage calc) must hit the decoy too, or the shared absorb helper has a leak.
    [Fact]
    public async Task DecoyAbsorbsNonStandardDamageCategories()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 200;
        var attacker = TestCreatures.Make("A", level: 50); // Seismic Toss deals 50 (= level)

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("seismic-toss"));

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(300, result.Defender.Attributes.HP); // user untouched
        Assert.Equal(150, result.Defender.Battle.SubstituteHp); // decoy took the 50
    }

    [Fact]
    public async Task DecoyBreaksWhenDepletedAndOverflowIsLost()
    {
        var defender = TestCreatures.Make("D", hp: 300, defense: 100);
        defender.Battle.SubstituteHp = 1; // about to break; the incoming hit dwarfs it
        var attacker = TestCreatures.Make("A", attack: 150);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("tackle"));

        Assert.True(result.Has<SubstituteBroke>());
        Assert.Equal(0, result.Defender.Battle.SubstituteHp);
        // Gen 1: the overflow past the decoy's HP is NOT applied to the user.
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(300, result.Defender.Attributes.HP);
    }

    [Fact]
    public async Task FailsWhenASubstituteIsAlreadyUp()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Battle.SubstituteHp = 51;

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.Equal("A", result.First<SubstituteAlreadyUp>()?.CreatureName);
        Assert.False(result.Has<MoveMissed>());
        Assert.False(result.Has<SubstituteCreated>());
        Assert.Equal(51, result.Attacker.Battle.SubstituteHp); // unchanged
        Assert.Equal(200, result.Attacker.Attributes.HP); // no extra HP paid
    }

    [Fact]
    public async Task FailsWhenHpTooLowToPayTheCost()
    {
        var user = TestCreatures.Make("A", hp: 200); // cost is 50
        user.Attributes.ReceiveDamage(160); // down to 40 (< cost)

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.True(result.Has<SubstituteTooWeak>());
        Assert.False(result.Has<MoveMissed>());
        Assert.Equal(0, result.Attacker.Battle.SubstituteHp); // no decoy raised
        Assert.Equal(40, result.Attacker.Attributes.HP); // HP unchanged
    }

    [Fact]
    public async Task AStandingSubstituteOutranksTooWeakAndLeavesTheByteAlone()
    {
        var user = TestCreatures.Make("A", hp: 200); // cost is 50
        user.Attributes.ReceiveDamage(160);
        user.Battle.SubstituteHp = 51;
        user.Battle.SubstituteRemnant = 7;

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.True(result.Has<SubstituteAlreadyUp>());
        Assert.False(result.Has<SubstituteTooWeak>());
        Assert.Equal(7, result.Attacker.Battle.SubstituteRemnant);
    }

    // pokered substitute.asm fails only on an underflow, so paying the user's last HP still raises the decoy.
    [Fact]
    public async Task SucceedsAtExactlyTheCostAndLeavesTheUserAtZeroHp()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Attributes.ReceiveDamage(150); // down to 50 (= cost)

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.True(result.Has<SubstituteCreated>());
        Assert.Equal(51, result.Attacker.Battle.SubstituteHp);
        Assert.Equal(0, result.Attacker.Attributes.HP);
        Assert.False(result.Attacker.IsAlive());
    }

    [Fact]
    public async Task ShieldsTheUserFromAPoisonMoveWithDidntAffect()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("toxic"));

        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
        Assert.False(result.Has<StatusApplied>());
        Assert.True(result.Has<MoveDidntAffect>());
    }

    [Theory]
    [InlineData("sleep-powder", StatusCondition.Sleep)]
    [InlineData("hypnosis", StatusCondition.Sleep)]
    [InlineData("thunder-wave", StatusCondition.Paralysis)]
    [InlineData("stun-spore", StatusCondition.Paralysis)]
    [InlineData("glare", StatusCondition.Paralysis)]
    public async Task SleepAndParalysisMovesLandThroughASubstitute(
        string move,
        StatusCondition expected
    )
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move(move));

        Assert.Equal(expected, result.Defender.Battle.Status);
        Assert.Equal(150, result.Defender.Battle.SubstituteHp);
    }

    [Theory]
    [InlineData("confuse-ray")]
    [InlineData("growl")]
    public async Task AConfusionOrStatDownMoveOnASubstituteSaysButItFailed(string move)
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move(move));

        Assert.True(result.Has<MoveFailed>());
        Assert.Equal(0, result.Defender.Battle.ConfusedTurns);
        Assert.Equal(0, result.Defender.Battle.Stages.Attack);
    }

    [Fact]
    public async Task GlareParalyzesANormalTypeBecauseAPureMoveHasNoSameTypeCheck()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Normal, hp: 300);

        var result = await new MoveScenario().Defender(defender).Use(Move("glare"));

        Assert.Equal(StatusCondition.Paralysis, result.Defender.Battle.Status);
        Assert.False(result.Has<MoveDidntAffect>());
    }

    [Fact]
    public async Task ASameTypeSecondaryStillCannotParalyze()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Normal, hp: 5000);

        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(defender)
            .Use(Move("body-slam"));

        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
    }

    private sealed class SubstituteStopsAllStatusRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override bool SubstituteStopsPureStatusMove(Attack move) => true;
    }

    [Fact]
    public async Task TheRulesCanMakeASubstituteStopParalysisMoves()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario()
            .Rules(new SubstituteStopsAllStatusRules())
            .Defender(defender)
            .Use(Move("thunder-wave"));

        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
    }

    private sealed class ThawThroughSubstituteRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override bool SubstituteStopsThaw => false;
    }

    private async Task<MoveResult> EmberAFrozenSubstituteHolder(IBattleRules rules)
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.Status = StatusCondition.Freeze;
        defender.Battle.SubstituteHp = 5000;
        return await new MoveScenario().Rules(rules).Defender(defender).Use(Move("ember"));
    }

    [Fact]
    public async Task AFireHitDoesNotThawAFrozenTargetBehindASubstitute()
    {
        var result = await EmberAFrozenSubstituteHolder(AlwaysHitRules.Instance);

        Assert.Equal(StatusCondition.Freeze, result.Defender.Battle.Status);
        Assert.False(result.Has<StatusCleared>());
    }

    [Fact]
    public async Task TheRulesCanLetAFireHitThawThroughASubstitute()
    {
        var result = await EmberAFrozenSubstituteHolder(new ThawThroughSubstituteRules());

        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
    }

    [Fact]
    public async Task ShieldsTheUserFromTheFoesStatDrop()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("growl")); // −Attack

        Assert.Equal(0, result.Defender.Battle.Stages.Attack);
        Assert.False(result.Has<StatStageChanged>());
    }

    [Fact]
    public async Task ShieldsTheUserFromTheFoesConfusion()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 150;

        var result = await new MoveScenario().Defender(defender).Use(Move("supersonic"));

        Assert.Equal(0, result.Defender.Battle.ConfusedTurns);
        Assert.False(result.Has<ConfusionStarted>());
    }

    // Gen 1: the shield holds even on the exact hit that breaks the decoy — a damaging move's
    // secondary status struck the substitute, so it doesn't reach the user, despite the decoy shattering
    // that turn. (Pinned because the shield is snapshotted at impact, before absorption zeroes the sub.)
    [Fact]
    public async Task SecondaryStatusIsBlockedEvenOnTheHitThatBreaksTheDecoy()
    {
        var defender = TestCreatures.Make("D", type1: DamageType.Water, hp: 300);
        defender.Battle.SubstituteHp = 1; // the incoming Body Slam shatters it this hit

        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance) // forces the paralysis chance to "land"
            .Defender(defender)
            .Use(Move("body-slam"));

        Assert.True(result.Has<SubstituteBroke>(), "the decoy broke on this hit");
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status); // …but the paralysis was shielded
        Assert.False(result.Has<StatusApplied>());
    }

    // Full-Battle proof: the decoy persists across turns, soaks the enemy's real attacks on later
    // turns, and protects the user's HP until it breaks. The player only knows Substitute (re-using it
    // while one is up just fails), so the enemy whittles the decoy then the user, terminating the battle.
    [Fact]
    public async Task DecoyPersistsAcrossTurnsAndProtectsHpUntilItBreaks()
    {
        // Deterministic damage (no miss, no crit, no variance) keeps the battle short and reproducible:
        // HP 210 (cost 52) leaves 2 HP after the fourth decoy, so the user can't afford a fifth and takes a real
        // hit; at an exact multiple it would pay its last HP and faint instead (Gen 1 exact-cost rule).
        var player = TestCreatures.Make("Player", hp: 210, speed: 200, defense: 80);
        player.AddAttack(Move("substitute")); // only move ⇒ AutoSelect always picks it

        var enemy = TestCreatures.Make("Enemy", type1: DamageType.Water, attack: 110, speed: 1);
        enemy.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 50,
                Accuracy = 100,
                DamageType = DamageType.Normal,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        var events = emitter.Events.ToList();
        // The player re-raises the decoy each time it breaks (paying the cost again) until it can no
        // longer afford it — so a Substitute goes up at least once and soaks real enemy hits on later turns.
        Assert.Contains(events, e => e is SubstituteCreated);
        Assert.Contains(events, e => e is SubstituteAbsorbedHit);

        int brokeIdx = events.FindIndex(e => e is SubstituteBroke);
        int playerHitIdx = events.FindIndex(e => e is DamageDealt d && d.TargetName == "Player");
        Assert.True(brokeIdx >= 0, "the decoy eventually broke");
        // The user only takes real HP damage once it can no longer keep a decoy up — never while one
        // stands (the decoy soaks everything). So the first hit on the user follows a broken decoy.
        Assert.True(
            playerHitIdx > brokeIdx,
            "the user took no real damage until the decoy broke (Gen 1)"
        );
        Assert.False(player.IsAlive());
    }

    private static Attack Tackle(int power) =>
        new()
        {
            Name = "Tackle",
            BaseDamage = power,
            Accuracy = 100,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Physical,
        };

    // pokered core.asm:424-447 runs the faint handler right after the self-KO, before the foe acts.
    [Fact]
    public async Task PlayerSubstituteAtExactlyTheCostFaintsTheLeadAndTheBenchComesIn()
    {
        var lead = TestCreatures.Make("Lead", hp: 200, speed: 200);
        lead.Attributes.ReceiveDamage(150); // HP 50 = cost
        lead.AddAttack(Move("substitute"));
        var bench = TestCreatures.Make("Bench", hp: 300, attack: 999, speed: 150);
        bench.AddAttack(Tackle(120));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make("Enemy", type1: DamageType.Water, hp: 60, speed: 1);
        enemy.AddAttack(Tackle(50));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter,
            playerParty: party,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        Assert.Contains(emitter.Of<SubstituteCreated>(), e => e.CreatureName == "Lead");
        Assert.Contains(emitter.Of<CreatureFainted>(), e => e.Name == "Lead");
        Assert.DoesNotContain(emitter.Of<DamageDealt>(), d => d.TargetName == "Lead");
        Assert.Equal("Bench", Assert.Single(emitter.Of<CreatureSwitchedIn>()).Name);
        Assert.True(battle.PlayerWon);
    }

    [Fact]
    public async Task EnemySubstituteAtExactlyTheCostFaintsTheFoeAndThePlayerWins()
    {
        var player = TestCreatures.Make("Player", hp: 200, speed: 1);
        player.AddAttack(Tackle(50));
        var enemy = TestCreatures.Make("Enemy", hp: 200, speed: 200);
        enemy.Attributes.ReceiveDamage(150); // HP 50 = cost
        enemy.AddAttack(Move("substitute"));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        Assert.True(battle.PlayerWon);
        Assert.Contains(emitter.Of<CreatureFainted>(), e => e.Name == "Enemy");
        Assert.Empty(emitter.Of<DamageDealt>());
        Assert.NotEmpty(emitter.Of<ExperienceGained>());
    }

    // --- Self-inflicted damage behind a Substitute (pokered core.asm AttackSubstitute) ---

    private class SelfHitRules(int firstSelfHitRoll = 1) : DelegatingBattleRules
    {
        private int _rolls;

        public override int ConfusionSelfHitPercent => ++_rolls >= firstSelfHitRoll ? 100 : 0;

        public override int RollConfusionTurns() => 8;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override double RollDamageVariance() => 1.0;

        public override (double Min, double Max) DamageVarianceRange => (1.0, 1.0);
    }

    private sealed class SelfInflictedIgnoresSubstituteRules : SelfHitRules
    {
        public override bool SelfInflictedDamageStrikesFoeSubstitute => false;
    }

    private sealed class ZeroCrashRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 0;

        public override int CalculateCrashDamage(Creature user) => 0;
    }

    private static Creature SelfHitter(int substituteHp)
    {
        var user = TestCreatures.Make("User", hp: 400, attack: 50, defense: 100);
        user.Battle.SubstituteHp = substituteHp;
        user.Battle.ConfusedTurns = 3;
        return user;
    }

    private static RecordingEmitter ForceSelfHit(
        Creature user,
        Creature foe,
        IBattleRules? rules = null
    )
    {
        var emitter = new RecordingEmitter();
        bool acted = StatusResolver.CanAct(
            user,
            rules ?? new SelfHitRules(),
            emitter,
            new SeededRandomSource(1),
            foe
        );
        Assert.False(acted);
        return emitter;
    }

    private static void AssertUserUntouched(Creature user, int substituteHp)
    {
        Assert.Equal(400, user.Attributes.HP);
        Assert.Equal(substituteHp, user.Battle.SubstituteHp);
    }

    [Fact]
    public void SelfHitBehindASubstituteDamagesTheFoesSubstitute()
    {
        var user = SelfHitter(substituteHp: 51);
        var foe = TestCreatures.Make("Foe", hp: 300);
        foe.Battle.SubstituteHp = 200;
        foe.Battle.SubstituteRemnant = 199;

        var emitter = ForceSelfHit(user, foe);

        int damage = Assert.Single(emitter.Of<ConfusionDamage>()).Damage;
        AssertUserUntouched(user, 51);
        Assert.Equal(200 - damage, foe.Battle.SubstituteHp);
        Assert.Equal(199 - damage, foe.Battle.SubstituteRemnant);
        Assert.Equal(300, foe.Attributes.HP);
        var absorbed = Assert.Single(emitter.Of<SubstituteAbsorbedHit>());
        Assert.Equal("Foe", absorbed.CreatureName);
        Assert.Equal(200 - damage, absorbed.SubstituteHpAfter);
        Assert.Empty(emitter.Of<SubstituteBroke>());
    }

    [Fact]
    public void SelfHitBreaksTheFoesSubstituteWithBothLinesInOrder()
    {
        var user = SelfHitter(substituteHp: 51);
        var foe = TestCreatures.Make("Foe", hp: 300);
        foe.Battle.SubstituteHp = 1;

        var emitter = ForceSelfHit(user, foe);

        AssertUserUntouched(user, 51);
        Assert.Equal(0, foe.Battle.SubstituteHp);
        Assert.Equal(300, foe.Attributes.HP);
        AssertStruckThenBroke(emitter);
    }

    private static void AssertStruckThenBroke(RecordingEmitter emitter)
    {
        var events = emitter.Events.ToList();
        int absorbed = events.FindIndex(e =>
            e is SubstituteAbsorbedHit a && a.CreatureName == "Foe"
        );
        int broke = events.FindIndex(e => e is SubstituteBroke b && b.CreatureName == "Foe");
        Assert.True(absorbed >= 0, "the took-damage line prints first");
        Assert.True(broke > absorbed, "the break line follows it");
    }

    private sealed class AlternateLinesRules : SelfHitRules
    {
        public override SubstituteBreakAnnouncement SubstituteBreakAnnouncement =>
            SubstituteBreakAnnouncement.TookDamageThenFaded;

        public override SubstituteSetupAnnouncement SubstituteSetupAnnouncement =>
            SubstituteSetupAnnouncement.UserMade;

        public override SubstituteFailureAnnouncement SubstituteFailureAnnouncement =>
            SubstituteFailureAnnouncement.Failed;
    }

    private async Task<MoveResult> BreakFoesDecoy(IBattleRules? rules = null)
    {
        var foe = TestCreatures.Make("Foe", hp: 300, defense: 100);
        foe.Battle.SubstituteHp = 1;
        var scenario = new MoveScenario()
            .Attacker(TestCreatures.Make("A", attack: 150))
            .Defender(foe);
        return await (rules is null ? scenario : scenario.Rules(rules)).Use(Move("tackle"));
    }

    [Fact]
    public async Task ABreakingHitPrintsTheTookDamageLineBeforeTheBreakLine()
    {
        var result = await BreakFoesDecoy();

        AssertStruckThenBroke(result.Emitter);
        Assert.False(result.Has<SubstituteFaded>());
    }

    [Fact]
    public async Task TheRulesPickTheFadedLineOnABreak()
    {
        var result = await BreakFoesDecoy(new AlternateLinesRules());

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.True(result.Has<SubstituteFaded>());
        Assert.False(result.Has<SubstituteBroke>());
    }

    [Fact]
    public void TheRulesPickTheFadedLineOnASelfHitBreak()
    {
        var user = SelfHitter(substituteHp: 51);
        var foe = TestCreatures.Make("Foe", hp: 300);
        foe.Battle.SubstituteHp = 1;

        var emitter = ForceSelfHit(user, foe, new AlternateLinesRules());

        Assert.Single(emitter.Of<SubstituteFaded>());
        Assert.Empty(emitter.Of<SubstituteBroke>());
    }

    private class CritHitRules : SelfHitRules
    {
        public override double GetCritChance(Creature a, Attack m) => 1.0;
    }

    private sealed class ThreeHitCritRules : CritHitRules
    {
        public override int RollMultiHitCount() => 3;
    }

    private sealed class NoSoakedLinesRules : CritHitRules
    {
        public override bool SoakedHitPrintsCritAndEffectiveness => false;
    }

    private async Task<MoveResult> HitDecoy(
        IBattleRules rules,
        string move = "tackle",
        int decoyHp = 5000,
        DamageType defenderType = DamageType.Normal
    )
    {
        var foe = TestCreatures.Make("Foe", type1: defenderType, hp: 300);
        foe.Battle.SubstituteHp = decoyHp;
        return await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Defender(foe)
            .Rules(rules)
            .Use(Move(move));
    }

    [Fact]
    public async Task ASoakedCritPrintsTheCritLineThoughTheCreatureTookNothing()
    {
        var result = await HitDecoy(new CritHitRules());

        var report = result.First<SoakedHitReport>()!;
        Assert.True(report.PrintsCritLine);
        Assert.Equal(1.0, report.TypeEffectiveness);
        Assert.Equal(300, result.Defender.Attributes.HP);
        Assert.False(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task TheCritLineFollowsTheBreakLine()
    {
        var result = await HitDecoy(new CritHitRules(), decoyHp: 1);

        var events = result.Events.ToList();
        int broke = events.FindIndex(e => e is SubstituteBroke);
        int report = events.FindIndex(e => e is SoakedHitReport);
        Assert.True(broke >= 0);
        Assert.True(report > broke, "the crit line follows the break line");
    }

    [Fact]
    public async Task ASoakedNonNeutralHitReportsItsEffectiveness()
    {
        var result = await HitDecoy(new SelfHitRules(), "water-gun", defenderType: DamageType.Fire);

        var report = result.First<SoakedHitReport>()!;
        Assert.False(report.PrintsCritLine);
        Assert.Equal(2.0, report.TypeEffectiveness);
    }

    [Fact]
    public async Task ANeutralNonCritSoakAddsNoLines()
    {
        var result = await HitDecoy(new SelfHitRules());

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<SoakedHitReport>());
    }

    [Fact]
    public async Task OnASoakedMultiHitTheCritLinePrintsOnceAndEffectivenessRepeats()
    {
        var result = await HitDecoy(
            new ThreeHitCritRules(),
            "fury-attack",
            defenderType: DamageType.Rock
        );

        var reports = result.Emitter.Of<SoakedHitReport>().ToList();
        Assert.Equal(3, reports.Count);
        Assert.Equal([true, false, false], reports.Select(r => r.PrintsCritLine));
        Assert.All(reports, r => Assert.Equal(0.5, r.TypeEffectiveness));
    }

    [Fact]
    public void ASelfHitOnTheFoesSubstituteAddsNoCritOrEffectivenessLines()
    {
        var user = SelfHitter(substituteHp: 51);
        var foe = TestCreatures.Make("Foe", hp: 300);
        foe.Battle.SubstituteHp = 200;

        var emitter = ForceSelfHit(user, foe, new CritHitRules());

        Assert.Single(emitter.Of<SubstituteAbsorbedHit>());
        Assert.Empty(emitter.Of<SoakedHitReport>());
    }

    private sealed class FirstStrikeEffectivenessRules : SelfHitRules
    {
        public override int RollMultiHitCount() => 3;

        public override bool EffectivenessLineRepeatsOnEveryStrike => false;
    }

    private sealed class LongWordingRules : SelfHitRules
    {
        public override double GetCritChance(Creature a, Attack m) => 1.0;

        public override HitLineWording HitLineWording => HitLineWording.Long;
    }

    [Fact]
    public async Task TheRulesCanLimitASoakedEffectivenessLineToTheFirstStrike()
    {
        var result = await HitDecoy(
            new FirstStrikeEffectivenessRules(),
            "fury-attack",
            defenderType: DamageType.Rock
        );

        var report = Assert.Single(result.Emitter.Of<SoakedHitReport>());
        Assert.True(report.PrintsEffectivenessLine);
    }

    [Fact]
    public async Task TheWordingOnBothHitEventsComesFromTheRules()
    {
        var soaked = await HitDecoy(new LongWordingRules());
        var real = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Defender(TestCreatures.Make("D", hp: 5000))
            .Rules(new LongWordingRules())
            .Use(Move("tackle"));

        Assert.Equal(HitLineWording.Long, soaked.First<SoakedHitReport>()!.Wording);
        Assert.Equal(HitLineWording.Long, real.First<DamageDealt>()!.Wording);
    }

    [Fact]
    public async Task AnImmuneOhkoLeavesASubstituteAloneAndAddsNoLines()
    {
        var result = await HitDecoy(new SelfHitRules(), "fissure", defenderType: DamageType.Flying);

        Assert.False(result.Has<SoakedHitReport>());
        Assert.False(result.Has<SubstituteAbsorbedHit>());
        Assert.Equal(5000, result.Defender.Battle.SubstituteHp);
    }

    [Fact]
    public async Task AFixedDamageMoveOnAResistingSubstituteHolderAddsNoLines()
    {
        var result = await HitDecoy(
            new SelfHitRules(),
            "sonic-boom",
            defenderType: DamageType.Rock
        );

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<SoakedHitReport>());
    }

    [Fact]
    public async Task TheRulesCanSilenceTheSoakedLines()
    {
        var result = await HitDecoy(new NoSoakedLinesRules());

        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<SoakedHitReport>());
    }

    [Fact]
    public async Task TheRulesPickTheFailedLineWhenASubstituteIsAlreadyUp()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Battle.SubstituteHp = 51;

        var result = await new MoveScenario()
            .Attacker(user)
            .Rules(new AlternateLinesRules())
            .Use(Move("substitute"));

        Assert.True(result.Has<MoveFailed>());
        Assert.False(result.Has<SubstituteAlreadyUp>());
    }

    [Fact]
    public async Task TheRulesPickTheFailedLineWhenTooWeak()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Attributes.ReceiveDamage(160);

        var result = await new MoveScenario()
            .Attacker(user)
            .Rules(new AlternateLinesRules())
            .Use(Move("substitute"));

        Assert.True(result.Has<MoveFailed>());
        Assert.False(result.Has<SubstituteTooWeak>());
    }

    [Fact]
    public async Task TheRulesPickTheMadeLineOnSetup()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", hp: 200))
            .Rules(new AlternateLinesRules())
            .Use(Move("substitute"));

        Assert.True(result.Has<SubstituteMade>());
        Assert.False(result.Has<SubstituteCreated>());
    }

    [Fact]
    public void SelfHitWithNoFoeSubstituteAndAByteBelowTheDamagePrintsTheBreak()
    {
        var user = SelfHitter(substituteHp: 51);
        var foe = TestCreatures.Make("Foe", hp: 300);

        var emitter = ForceSelfHit(user, foe);

        int damage = Assert.Single(emitter.Of<ConfusionDamage>()).Damage;
        AssertUserUntouched(user, 51);
        Assert.Equal(300, foe.Attributes.HP);
        Assert.Equal(0, foe.Battle.SubstituteHp);
        Assert.Equal(256 - damage, foe.Battle.SubstituteRemnant);
        AssertStruckThenBroke(emitter);
    }

    [Fact]
    public void SelfHitWithNoFoeSubstituteAndAByteAboveTheDamageOnlyDrainsTheByte()
    {
        var user = SelfHitter(substituteHp: 51);
        var foe = TestCreatures.Make("Foe", hp: 300);
        foe.Battle.SubstituteRemnant = 200;

        var emitter = ForceSelfHit(user, foe);

        int damage = Assert.Single(emitter.Of<ConfusionDamage>()).Damage;
        AssertUserUntouched(user, 51);
        Assert.Equal(200 - damage, foe.Battle.SubstituteRemnant);
        Assert.Equal(0, foe.Battle.SubstituteHp);
        Assert.Single(emitter.Of<SubstituteAbsorbedHit>());
        Assert.Empty(emitter.Of<SubstituteBroke>());
    }

    [Fact]
    public void SelfHitWithoutAUserSubstituteHitsTheUserAndLeavesTheFoesSubstituteAlone()
    {
        var user = SelfHitter(substituteHp: 0);
        var foe = TestCreatures.Make("Foe", hp: 300);
        foe.Battle.SubstituteHp = 200;

        var emitter = ForceSelfHit(user, foe);

        int damage = Assert.Single(emitter.Of<ConfusionDamage>()).Damage;
        Assert.Equal(400 - damage, user.Attributes.HP);
        Assert.Equal(200, foe.Battle.SubstituteHp);
        Assert.Empty(emitter.Of<SubstituteAbsorbedHit>());
    }

    [Fact]
    public void SelfHitBehindASubstituteHitsTheUserWhenTheRulesIgnoreSubstitutes()
    {
        var user = SelfHitter(substituteHp: 51);
        var foe = TestCreatures.Make("Foe", hp: 300);
        foe.Battle.SubstituteHp = 200;

        var emitter = ForceSelfHit(user, foe, new SelfInflictedIgnoresSubstituteRules());

        int damage = Assert.Single(emitter.Of<ConfusionDamage>()).Damage;
        Assert.Equal(400 - damage, user.Attributes.HP);
        Assert.Equal(51, user.Battle.SubstituteHp);
        Assert.Equal(200, foe.Battle.SubstituteHp);
    }

    [Theory]
    [InlineData("jump-kick")]
    [InlineData("high-jump-kick")]
    public async Task CrashBehindASubstituteDamagesTheFoesSubstitute(string moveName)
    {
        var user = TestCreatures.Make("User", hp: 400);
        user.Battle.SubstituteHp = 51;
        var foe = TestCreatures.Make("Foe", hp: 300);
        foe.Battle.SubstituteHp = 200;

        var result = await new MoveScenario()
            .Attacker(user)
            .Defender(foe)
            .Rules(NeverHitRules.Instance)
            .Use(Move(moveName));

        var crash = result.First<CrashDamage>();
        Assert.NotNull(crash);
        Assert.Equal(400, crash!.HpAfter);
        AssertUserUntouched(user, 51);
        Assert.Equal(200 - crash.Damage, foe.Battle.SubstituteHp);
        Assert.True(result.Has<SubstituteAbsorbedHit>());
    }

    [Fact]
    public async Task CrashWithNoFoeSubstitutePrintsTheBreakAndLeavesTheUserAlone()
    {
        var user = TestCreatures.Make("User", hp: 400);
        user.Battle.SubstituteHp = 51;
        var foe = TestCreatures.Make("Foe", hp: 300);

        var result = await new MoveScenario()
            .Attacker(user)
            .Defender(foe)
            .Rules(NeverHitRules.Instance)
            .Use(Move("jump-kick"));

        AssertUserUntouched(user, 51);
        Assert.Equal(300, foe.Attributes.HP);
        Assert.True(result.Has<SubstituteAbsorbedHit>());
        Assert.True(result.Has<SubstituteBroke>());
    }

    [Fact]
    public async Task ZeroCrashDamageBehindASubstituteStrikesNothing()
    {
        var user = TestCreatures.Make("User", hp: 400);
        user.Battle.SubstituteHp = 51;

        var result = await new MoveScenario()
            .Attacker(user)
            .Rules(new ZeroCrashRules())
            .Use(Move("jump-kick"));

        Assert.True(result.Has<CrashDamage>());
        Assert.False(result.Has<SubstituteAbsorbedHit>());
        Assert.False(result.Has<SubstituteBroke>());
    }

    [Fact]
    public async Task OrdinaryRecoilStillHitsTheUserBehindASubstitute()
    {
        var user = TestCreatures.Make("User", hp: 400);
        user.Battle.SubstituteHp = 51;
        var foe = TestCreatures.Make("Foe", type1: DamageType.Water, hp: 300);

        var result = await new MoveScenario().Attacker(user).Defender(foe).Use(Move("double-edge"));

        Assert.True(result.Attacker.Attributes.HP < 400);
        Assert.Equal(51, result.Attacker.Battle.SubstituteHp);
    }

    [Theory]
    [InlineData(50, 10, 40, false)]
    [InlineData(50, 50, 0, false)]
    [InlineData(10, 50, 216, true)]
    [InlineData(0, 3, 253, true)]
    [InlineData(50, 256, 50, true)]
    public void Gen1SubstituteByteWrapsAndBreaksOnlyOnAnUnderflow(
        int remnant,
        int damage,
        int expectedRemnant,
        bool expectedBroke
    )
    {
        var strike = Gen1BattleRules.Instance.StrikeSubstituteByte(remnant, damage);

        Assert.Equal(expectedRemnant, strike.Remnant);
        Assert.Equal(expectedBroke, strike.Broke);
    }

    [Fact]
    public async Task PuttingUpASubstituteWritesTheByte()
    {
        var user = TestCreatures.Make("A", hp: 200);

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.Equal(50, result.Attacker.Battle.SubstituteRemnant);
    }

    [Fact]
    public async Task ATooWeakSubstituteStillWritesTheByte()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Attributes.ReceiveDamage(160);

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.False(result.Has<SubstituteCreated>());
        Assert.Equal(50, result.Attacker.Battle.SubstituteRemnant);
    }

    [Fact]
    public async Task ARedundantSubstituteLeavesTheByteAlone()
    {
        var user = TestCreatures.Make("A", hp: 200);
        user.Battle.SubstituteHp = 51;
        user.Battle.SubstituteRemnant = 7;

        var result = await new MoveScenario().Attacker(user).Use(Move("substitute"));

        Assert.Equal(7, result.Attacker.Battle.SubstituteRemnant);
    }

    [Fact]
    public async Task ABreakingHitLeavesTheWrappedByte()
    {
        var defender = TestCreatures.Make("D", hp: 300);
        defender.Battle.SubstituteHp = 11;
        defender.Battle.SubstituteRemnant = 10;
        var attacker = TestCreatures.Make("A", level: 50);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("seismic-toss"));

        Assert.True(result.Has<SubstituteBroke>());
        Assert.Equal(216, result.Defender.Battle.SubstituteRemnant);
    }

    [Fact]
    public async Task TheByteSurvivesASwitchAndAbsorbsTheFoesSelfHit()
    {
        var lead = TestCreatures.Make("Lead", hp: 400, speed: 200);
        lead.AddAttack(Move("supersonic"));
        lead.AddAttack(Move("substitute"));
        var bench = TestCreatures.Make("Bench", hp: 400, attack: 999, speed: 150);
        bench.AddAttack(Tackle(120));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make(
            "Enemy",
            type1: DamageType.Water,
            hp: 40,
            attack: 50,
            defense: 100,
            speed: 1
        );
        enemy.AddAttack(Move("substitute"));
        enemy.AddAttack(Move("growl"));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput("supersonic", "substitute", "Tackle").TurnPlan(null, null, 1),
            new ScriptedInput("substitute", "growl"),
            rules: new SelfHitRules(firstSelfHitRoll: 3),
            emitter: emitter,
            playerParty: party,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        AssertBenchByteAbsorbedTheSelfHit(emitter, bench);
    }

    [Fact]
    public async Task TheByteSurvivesAFaintSwitchAndAbsorbsTheFoesSelfHit()
    {
        var lead = TestCreatures.Make("Lead", hp: 400, speed: 200);
        lead.Attributes.ReceiveDamage(300); // HP 100 = cost, so raising the decoy faints it
        lead.AddAttack(Move("supersonic"));
        lead.AddAttack(Move("substitute"));
        var bench = TestCreatures.Make("Bench", hp: 400, attack: 999, speed: 1);
        bench.AddAttack(Tackle(120));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make(
            "Enemy",
            type1: DamageType.Water,
            hp: 40,
            attack: 50,
            defense: 100,
            speed: 5
        );
        enemy.AddAttack(Move("substitute"));
        enemy.AddAttack(Move("growl"));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput("supersonic", "substitute", "Tackle"),
            new ScriptedInput("substitute", "growl"),
            rules: new SelfHitRules(firstSelfHitRoll: 2),
            emitter: emitter,
            playerParty: party,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        Assert.Contains(emitter.Of<CreatureFainted>(), e => e.Name == "Lead");
        AssertBenchByteAbsorbedTheSelfHit(emitter, bench);
    }

    private static void AssertBenchByteAbsorbedTheSelfHit(RecordingEmitter emitter, Creature bench)
    {
        var events = emitter.Events.ToList();
        int selfHit = events.FindIndex(e => e is ConfusionDamage);
        var confusion = Assert.IsType<ConfusionDamage>(events[selfHit]);
        var absorbed = Assert.IsType<SubstituteAbsorbedHit>(events[selfHit + 1]);
        Assert.Equal("Enemy", confusion.CreatureName);
        Assert.Equal(30, confusion.HpAfter);
        Assert.Equal("Bench", absorbed.CreatureName);
        Assert.Equal(100 - confusion.Damage, absorbed.SubstituteHpAfter);
        Assert.DoesNotContain(events, e => e is SubstituteBroke b && b.CreatureName == "Bench");
        Assert.Equal(400, bench.Attributes.HP);
    }

    [Fact]
    public async Task AnOhkoBreaksADecoyLargerThanTheTargetsHpAndLeavesTheByte()
    {
        var defender = TestCreatures.Make("D", hp: 200, speed: 1);
        defender.Attributes.ReceiveDamage(190); // HP 10, below the decoy's 51
        defender.Battle.SubstituteHp = 51;
        defender.Battle.SubstituteRemnant = 50;
        var attacker = TestCreatures.Make("A", speed: 200);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(defender)
            .Use(Move("guillotine"));

        Assert.True(result.Has<SubstituteBroke>());
        Assert.Equal(0, result.Defender.Battle.SubstituteHp);
        Assert.Equal(50, result.Defender.Battle.SubstituteRemnant);
        Assert.Equal(10, result.Defender.Attributes.HP);
    }

    [Fact]
    public async Task TheByteIsZeroInTheNextBattle()
    {
        var player = TestCreatures.Make("Player", attack: 999, speed: 200);
        player.AddAttack(Tackle(120));
        player.Battle.SubstituteRemnant = 77;
        var enemy = TestCreatures.Make("Enemy", type1: DamageType.Water, hp: 20, speed: 1);
        enemy.AddAttack(Tackle(10));

        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: NoVarianceNoCritHitRules.Instance,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        Assert.Equal(0, player.Battle.SubstituteRemnant);
    }
}
