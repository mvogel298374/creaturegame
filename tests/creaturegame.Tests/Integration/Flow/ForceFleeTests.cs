using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.Integration.Interactions;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// End-to-end contract for Roar / Whirlwind (<see cref="MoveEffect.ForceFlee"/>), driven through the real
/// <see cref="Battle"/> loop and <see cref="RunDirector"/> on the <b>imported</b> rows (the DB-row →
/// <c>ForceFlee</c> mapping + the effect-sets-the-flag step are pinned in
/// <c>UniqueMoveEffectContractTests</c>; here we prove what that flag does to a whole battle and a run):
/// <list type="bullet">
/// <item>A <b>wild</b> (escapable) battle ends in a flee — one <see cref="CreatureFled"/> instead of a
/// <see cref="BattleEnded"/>, no faint, no winner; <see cref="CreatureFled.IsPlayer"/> tracks which side
/// was scared off (the foe fleeing vs the player being blown away).</item>
/// <item>Against the trainer-analog tiers (Elite/Boss) the battle is <b>non-escapable</b>: the move just
/// fails and the fight continues to a normal KO.</item>
/// <item>In a run a flee advances the encounter as <b>neither a win nor a loss</b> — no XP, no win count.</item>
/// </list>
/// </summary>
[Collection(MovesCollection.Name)]
public class ForceFleeTests(MovesFixture moves) : InteractionTest(moves)
{
    // Wild battle: the player's Roar scares the foe off. The loop ends on the flee (no faint), so the stream
    // carries CreatureFled for the foe and NOT a BattleEnded — the run loop, not a win/loss screen, takes over.
    [Theory]
    [InlineData("roar")]
    [InlineData("whirlwind")]
    public async Task WildBattle_PlayerForceFlee_EndsInFlee_NotBattleEnded(string fleeMove)
    {
        var player = Mon("Player", hp: 300, attack: 50, speed: 200, DamageType.Normal, fleeMove);
        var enemy = Mon("Enemy", hp: 300, attack: 1, speed: 1, DamageType.Normal, "splash");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses(fleeMove)
            .EnemyUses("splash")
            .RunAsync();

        var fled = Assert.Single(result.All<CreatureFled>());
        Assert.Equal("Enemy", fled.Name);
        Assert.False(fled.IsPlayer); // the wild foe fled — not the player
        Assert.False(result.Has<BattleEnded>()); // a flee is announced via CreatureFled, never BattleEnded
        Assert.Equal("", result.Winner);
        Assert.True(player.IsAlive());
        Assert.True(enemy.Battle.HasFled);
    }

    // The other wording branch: when the ENEMY uses Roar the player is the one scared off, so CreatureFled
    // flags IsPlayer (the client says "… was blown away!" rather than "The wild … fled!").
    [Fact]
    public async Task WildBattle_EnemyForceFlee_ScaresPlayerOff_FlagsIsPlayer()
    {
        var player = Mon("Player", hp: 300, attack: 1, speed: 1, DamageType.Normal, "splash");
        var enemy = Mon("Enemy", hp: 300, attack: 1, speed: 200, DamageType.Normal, "roar");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("splash")
            .EnemyUses("roar")
            .RunAsync();

        var fled = Assert.Single(result.All<CreatureFled>());
        Assert.Equal("Player", fled.Name);
        Assert.True(fled.IsPlayer);
        Assert.False(result.Has<BattleEnded>());
        Assert.True(player.Battle.HasFled);
        Assert.DoesNotContain(result.All<MoveUsed>(), e => e.AttackerName == "Player");
    }

    // Non-escapable battle (the Elite/Boss trainer analog): Roar fails ("… is unaffected!") and the fight
    // continues — the player finishes the foe off with a real attack, so the battle ends as a normal win.
    [Fact]
    public async Task NonEscapableBattle_ForceFlee_Fails_BattleContinuesToKo()
    {
        var player = Mon(
            "Player",
            hp: 300,
            attack: 999,
            speed: 200,
            DamageType.Normal,
            "roar",
            "tackle"
        );
        var enemy = Mon("Enemy", hp: 1, attack: 1, speed: 1, DamageType.Normal, "splash");

        var result = await new BattleScenario()
            .Escapable(false)
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("roar", "tackle") // roar fails turn 1; tackle KOs turn 2
            .EnemyUses("splash")
            .RunAsync();

        Assert.False(result.Has<CreatureFled>()); // the move failed — nobody fled
        Assert.False(enemy.Battle.HasFled);
        Assert.Contains(result.All<MoveUnaffected>(), e => e.Cause == UnaffectedCause.ForceFlee);
        Assert.Equal("Player", result.Winner); // the fight ended on a normal KO, not a flee
        Assert.True(result.Has<BattleEnded>());
    }

    // In a run, a flee is neither a win nor a loss: it advances the chain (a second encounter starts) but adds
    // nothing to BattlesWon and awards no XP (nothing fainted). The run still ends only on a real faint.
    [Fact]
    public async Task Run_ForceFlee_AdvancesRun_WithoutCountingAsWinOrAwardingXp()
    {
        // 1 HP so the faster encounter-2 bruiser one-shots the player before it can act (every legacy-chain
        // battle is EncounterTier.Normal/escapable, so an un-KO'd player would just keep roaring foes away).
        var player = Mon("Player", hp: 1, attack: 50, speed: 200, DamageType.Normal, "roar");
        int startXp = player.Experience;

        // Encounter 1 is an escapable wild foe the player roars away (its Splash can't scratch the 1-HP
        // player); encounter 2 is a fast bruiser that one-shots the player and ends the run.
        int built = 0;
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) =>
        {
            built++;
            var enemy =
                built == 1
                    ? Mon("Wild", hp: 300, attack: 1, speed: 1, DamageType.Normal, "splash")
                    : Mon("Bruiser", hp: 999, attack: 999, speed: 999, DamageType.Normal, "tackle");
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("roar"),
            new ScriptedInput("splash", "tackle"),
            movePool: Array.Empty<Attack>(),
            new RunDirectorOptions
            {
                Emitter = recorder,
                Rules = new ScriptableRules().Deterministic(),
                Rng = new SeededRandomSource(0),
            }
        );

        await runner.RunAsync();

        Assert.True(recorder.Of<CreatureFled>().Any()); // encounter 1 ended in a flee
        Assert.Equal(2, recorder.Of<BattleStarted>().Count()); // the flee advanced to a second encounter
        var runEnded = Assert.Single(recorder.Of<RunEnded>());
        Assert.Equal(0, runEnded.BattlesWon); // a flee is not a win, and encounter 2 was a loss
        Assert.False(recorder.Of<ExperienceGained>().Any()); // nothing fainted on the flee → no XP
        Assert.Equal(startXp, player.Experience);
        Assert.False(player.IsAlive());
    }

    // The escapability flag must reach the move on the ALTERNATE execution path too: Metronome calling Roar in
    // a non-escapable battle must fail (MoveUnaffected, no flee), exactly as a directly-chosen Roar would —
    // not slip through on the inner action's default escapable=true. Pool's only eligible move is Roar, so the
    // Metronome pick is deterministic; the battle is non-escapable (the Elite/Boss trainer analog).
    [Fact]
    public async Task Metronome_CallingForceFlee_InNonEscapableBattle_Fails_AndNobodyFlees()
    {
        var attacker = Mon(
            "Player",
            hp: 300,
            attack: 50,
            speed: 100,
            DamageType.Normal,
            "metronome"
        );
        var defender = Mon("Enemy", hp: 300, attack: 1, speed: 1, DamageType.Normal, "splash");
        var pool = new List<Attack> { Move("metronome"), Move("roar") }; // only Roar is Metronome-eligible
        var recorder = new RecordingEmitter();

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            Gen1TypeChart.Instance,
            rules: AlwaysHitRules.Instance,
            emitter: recorder,
            movePool: pool,
            battleEscapable: false
        );
        await action.ExecuteAsync();

        Assert.Contains(recorder.Of<MoveUsed>(), e => e.MoveName == "roar"); // Metronome called Roar
        Assert.True(recorder.Of<MoveUnaffected>().Any()); // ... which failed in the non-escapable battle
        Assert.False(defender.Battle.HasFled); // the inner action honoured escapable=false — nobody fled
    }

    // ── Gen 1 turn end, level roll and accuracy (GENERATION_SEAMS.md §2) ──

    private static IBattleRules AltRules() =>
        TestAltProfile.Instance.BuildBattleRules(new SeededRandomSource(0));

    private static IBattleRules RealAccuracyRules() =>
        new ScriptableRules(new SeededRandomSource(0)).NoCrit().NoVariance();

    private async Task<(Battle Battle, RecordingEmitter Recorder, Creature Lead)> PoisonedLeadRoars(
        IBattleRules rules,
        int leadSpeed = 200
    )
    {
        var lead = TestFighters.Fighter("Lead", hp: 160, attack: 1, speed: leadSpeed, defense: 999);
        lead.Attributes.HP = 5; // the 10-point poison tick would be lethal
        lead.AddAttack(Move("roar"));
        var party = new Party(lead);
        party.Add(TestFighters.Fighter("Bench", hp: 300, attack: 999, speed: 150));
        var foe = TestFighters.Fighter("Foe", hp: 500, attack: 1, speed: 100);
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            foe,
            Gen1TypeChart.Instance,
            new ScriptedInput("roar", "tackle"),
            new ScriptedInput("tackle"),
            rules: rules,
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerEntryStatus: new CarriedStatus(StatusCondition.Poison, 0),
            escapable: true,
            playerParty: party
        );
        await battle.StartFightAsync();
        return (battle, recorder, lead);
    }

    [Fact]
    public async Task Gen1FasterRoarEndsTheTurnAtOnce_TheFoeNeverActsAndThePoisonNeverTicks()
    {
        var (battle, recorder, lead) = await PoisonedLeadRoars(
            new ScriptableRules().Deterministic()
        );

        Assert.True(battle.EndedInFlee);
        Assert.Equal("Foe", Assert.Single(recorder.Of<CreatureFled>()).Name);
        Assert.DoesNotContain(recorder.Of<MoveUsed>(), e => e.AttackerName == "Foe");
        Assert.Empty(recorder.Of<DamageDealt>());
        Assert.Equal(5, lead.Attributes.HP);
        Assert.Empty(recorder.Of<CreatureFainted>());
        Assert.Empty(recorder.Of<BattleEnded>());
    }

    [Fact]
    public async Task TheSeamDecidesWhetherAFleeEndsTheTurn()
    {
        var (battle, recorder, _) = await PoisonedLeadRoars(AltRules());

        Assert.Contains(recorder.Of<MoveUsed>(), e => e.AttackerName == "Foe");
        Assert.Contains(recorder.Of<CreatureFainted>(), e => e.Name == "Lead");
        Assert.False(battle.EndedInFlee);
        Assert.Empty(recorder.Of<CreatureSwitchedIn>()); // the Roar landed, so no switch-in
    }

    // The slower side's Roar also ends the turn: its own poison never ticks after it (core.asm:430-432).
    [Fact]
    public async Task Gen1SlowerRoarEndsTheTurnAtOnce_TheRoarersPoisonNeverTicks()
    {
        var (battle, recorder, lead) = await PoisonedLeadRoars(
            new ScriptableRules().Deterministic(),
            leadSpeed: 50
        );

        Assert.Single(recorder.Of<MoveUsed>(), e => e.AttackerName == "Foe");
        int tackled = recorder
            .Of<DamageDealt>()
            .Where(d => d.TargetName == "Lead")
            .Sum(d => d.Damage);
        Assert.Equal(5 - tackled, lead.Attributes.HP);
        Assert.True(lead.IsAlive());
        Assert.True(battle.EndedInFlee);
    }

    private Task<BattleScenarioResult> LowerLevelRoar(
        IRandomSource battleRng,
        IBattleRules? rules = null
    )
    {
        var player = Mon(
            "Player",
            hp: 300,
            attack: 999,
            speed: 200,
            DamageType.Normal,
            "roar",
            "tackle"
        );
        player.Level = 10;
        var enemy = Mon("Enemy", hp: 1, attack: 1, speed: 1, DamageType.Normal, "splash");
        var scenario = new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("roar", "tackle")
            .EnemyUses("splash")
            .BattleRng(battleRng);
        return (rules is null ? scenario : scenario.Rules(rules)).RunAsync();
    }

    [Fact]
    public async Task Gen1LowerLevelRoarFailsOnALowRoll_AndSaysItDidntAffect()
    {
        var result = await LowerLevelRoar(new LowestRollSource());

        var failed = Assert.Single(result.All<MoveDidntAffect>());
        Assert.Equal("Enemy", failed.TargetName);
        Assert.Equal("roar", failed.MoveName);
        Assert.False(result.Has<CreatureFled>());
        Assert.False(result.Has<MoveUnaffected>());
        Assert.Equal("Player", result.Winner);
    }

    [Fact]
    public async Task Gen1LowerLevelRoarStillSucceedsOnAHighRoll()
    {
        var result = await LowerLevelRoar(new HighestRollSource());

        Assert.False(result.Has<MoveDidntAffect>());
        Assert.Equal("Enemy", Assert.Single(result.All<CreatureFled>()).Name);
    }

    // pokered's .handleEnemy branch rolls the same way for a wild foe's Roar.
    [Fact]
    public async Task Gen1LowerLevelFoesRoarFailsOnALowRoll_AndThePlayerStays()
    {
        var player = Mon("Player", hp: 300, attack: 999, speed: 1, DamageType.Normal, "tackle");
        var enemy = Mon("Enemy", hp: 1, attack: 1, speed: 200, DamageType.Normal, "roar");
        enemy.Level = 10;

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("tackle")
            .EnemyUses("roar")
            .BattleRng(new LowestRollSource())
            .RunAsync();

        var failed = Assert.Single(result.All<MoveDidntAffect>());
        Assert.Equal("Player", failed.TargetName);
        Assert.False(result.Has<CreatureFled>());
        Assert.False(player.Battle.HasFled);
        Assert.Equal("Player", result.Winner);
    }

    // Roar's effect skips the hit test, so a Normal-type Roar still works on a Ghost-type.
    [Fact]
    public async Task Gen1WhirlwindBlowsAwayAGhostType_TypeImmunityDoesNotApply()
    {
        var player = Mon("Player", hp: 300, attack: 1, speed: 200, DamageType.Normal, "whirlwind");
        var enemy = Mon("Enemy", hp: 300, attack: 1, speed: 1, DamageType.Ghost, "splash");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("whirlwind")
            .EnemyUses("splash")
            .RunAsync();

        Assert.False(result.Has<MoveHadNoEffect>());
        Assert.Equal("Enemy", Assert.Single(result.All<CreatureFled>()).Name);
    }

    [Fact]
    public async Task TheSeamDecidesTheWildRollAndItsFailureLine()
    {
        var result = await LowerLevelRoar(new HighestRollSource(), AltRules());

        Assert.False(result.Has<CreatureFled>());
        Assert.False(result.Has<MoveDidntAffect>());
        Assert.Contains(result.All<MoveMissed>(), e => e.MoveName == "roar");
    }

    private Task<BattleScenarioResult> WhirlwindOnAMissingAccuracyRoll(IBattleRules rules)
    {
        var player = Mon("Player", hp: 300, attack: 1, speed: 200, DamageType.Normal, "whirlwind");
        var enemy = Mon("Enemy", hp: 300, attack: 1, speed: 1, DamageType.Normal, "splash");
        return new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("whirlwind")
            .EnemyUses("splash")
            .Rules(rules)
            .BattleRng(new HighestRollSource())
            .RunAsync();
    }

    [Fact]
    public async Task Gen1WhirlwindSkipsTheAccuracyRoll_SoAMissingRollStillBlowsTheFoeAway()
    {
        var result = await WhirlwindOnAMissingAccuracyRoll(RealAccuracyRules());

        Assert.DoesNotContain(result.All<MoveMissed>(), e => e.MoveName == "whirlwind");
        Assert.Equal("Enemy", Assert.Single(result.All<CreatureFled>()).Name);
    }

    private sealed class WhirlwindRollsAccuracyRules : DelegatingBattleRules
    {
        public override bool ForceFleeSkipsAccuracy => false;
    }

    [Fact]
    public async Task TheSeamDecidesWhetherWhirlwindRollsAccuracy()
    {
        var result = await WhirlwindOnAMissingAccuracyRoll(new WhirlwindRollsAccuracyRules());

        Assert.Contains(result.All<MoveMissed>(), e => e.MoveName == "whirlwind");
        Assert.False(result.Has<CreatureFled>());
    }

    private sealed class FixedRoll(int value) : IRandomSource
    {
        public int Draws { get; private set; }

        public int Next(int maxExclusive)
        {
            Draws++;
            return value;
        }

        public int Next(int minInclusive, int maxExclusive) => Next(maxExclusive);

        public double NextDouble() => throw new NotSupportedException();
    }

    // pokered SwitchAndTeleportEffect: rand[0, user + target] >= target / 4; Lv10 vs Lv50 → the cut is 12.
    [Theory]
    [InlineData(12, true)]
    [InlineData(11, false)]
    public void Gen1WildRollPassesAtAQuarterOfTheTargetsLevel(int roll, bool succeeds)
    {
        var rng = new FixedRoll(roll);

        Assert.Equal(succeeds, Gen1BattleRules.Instance.ForceFleeSucceeds(10, 50, rng));
        Assert.Equal(1, rng.Draws);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(60, 50)]
    public void Gen1RoarAtOrAboveTheTargetsLevelSucceedsWithoutARoll(int userLevel, int targetLevel)
    {
        var rng = new FixedRoll(0);

        Assert.True(Gen1BattleRules.Instance.ForceFleeSucceeds(userLevel, targetLevel, rng));
        Assert.Equal(0, rng.Draws);
    }
}
