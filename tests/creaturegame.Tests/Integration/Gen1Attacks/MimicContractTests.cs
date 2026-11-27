using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>Mimic (Gen 1): the pick, the copy and its revert; GEN_DIFFERENCES.md → Mimic.</summary>
[Collection(MovesCollection.Name)]
public class MimicContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task MimicCopiesAMoveFromTheTargetWithoutDamage()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("tackle"));

        var result = await new MoveScenario().Defender(defender).Use(Move("mimic"));

        Assert.False(result.Has<DamageDealt>(), "Mimic deals no damage the turn it copies");
        Assert.Equal("tackle", result.Move.Base.Name); // the Mimic slot has become Tackle

        var learned = result.First<MimicLearned>();
        Assert.NotNull(learned);
        Assert.Equal("tackle", learned!.MoveName);
    }

    // pokered effects.asm:1207-1210 and :1273-1274: a missed Mimic jumps to PrintButItFailedText_.
    [Fact]
    public async Task AMissedMimicPrintsButItFailedAndCopiesNothing()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("tackle"));

        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(defender)
            .Use(Move("mimic"));

        Assert.True(result.Has<MoveFailed>());
        Assert.False(result.Has<MoveMissed>());
        Assert.False(result.Has<MimicLearned>());
        Assert.Equal("mimic", result.Move.Base.Name);
    }

    [Fact]
    public async Task TheSeamPicksAMissedMimicsLine()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("tackle"));

        var result = await new MoveScenario()
            .Rules(new MissedLineNeverHitRules())
            .Defender(defender)
            .Use(Move("mimic"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<MoveFailed>());
    }

    private sealed class MissedLineNeverHitRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 0;

        public override MoveFailureAnnouncement PrimaryEffectFailureAnnouncement =>
            MoveFailureAnnouncement.Missed;
    }

    [Fact]
    public async Task MimicCopiesThroughASubstitute()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("tackle"));
        defender.Battle.SubstituteHp = 50;

        var result = await new MoveScenario().Defender(defender).Use(Move("mimic"));

        Assert.Equal("tackle", result.First<MimicLearned>()?.MoveName);
        Assert.False(result.Has<MoveFailed>());
        Assert.Equal(50, defender.Battle.SubstituteHp);
    }

    [Fact]
    public async Task MimicCopiesFromAGhostTypeDespiteBeingANormalMove()
    {
        var defender = TestCreatures.Make("D", hp: 500);
        defender.Type1 = DamageType.Ghost;
        defender.AddAttack(Move("tackle"));

        var result = await new MoveScenario().Defender(defender).Use(Move("mimic"));

        Assert.Equal("tackle", result.First<MimicLearned>()?.MoveName);
        Assert.False(result.Has<MoveHadNoEffect>());
    }

    [Fact]
    public async Task ResettingBattleStateRevertsAMimickedMove()
    {
        // Guards the battle-boundary reset: Battle calls ResetBattleState() on both creatures when a new
        // fight starts, which must revert the transient Mimic swap rather than orphan it — otherwise the
        // copied move leaks into the permanent MoveSet (the reset throws away the bookkeeping that
        // battle-end restore relies on). Haze does NOT do this mid-battle — see
        // HazeDoesNotRevertAMimickedMove below; pokered's HazeEffect_ never touches the Mimic slot.
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("tackle"));

        var result = await new MoveScenario().Defender(defender).Use(Move("mimic"));
        Assert.Equal("tackle", result.Move.Base.Name); // swapped in

        result.Attacker.ResetBattleState();
        Assert.Equal("mimic", result.Move.Base.Name); // reverted by the reset, not orphaned
    }

    [Fact]
    public async Task HazeDoesNotRevertAMimickedMove()
    {
        // Gen 1's HazeEffect_ resets stat mods and volatile statuses but never touches the Mimic move
        // slot — unlike a battle-boundary ResetBattleState() (see the test above), a mid-battle Haze
        // must leave an active Mimic swap alone.
        var defender = TestCreatures.Make("D", hp: 500);
        defender.AddAttack(Move("tackle"));

        var result = await new MoveScenario().Defender(defender).Use(Move("mimic"));
        Assert.Equal("tackle", result.Move.Base.Name); // swapped in

        result.Attacker.ResetForHaze(preserveMajorStatus: true);
        Assert.Equal("tackle", result.Move.Base.Name); // NOT reverted by Haze
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
        player.Attributes.Speed = 200; // outspeed the enemy
        player.AddAttack(Move("mimic"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 40;
        enemy.Attributes.Defense = 20;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(Move("tackle"));

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

        // It copied Tackle (the enemy's only move)...
        var learned = emitter.Of<MimicLearned>().FirstOrDefault();
        Assert.NotNull(learned);
        Assert.Equal("tackle", learned!.MoveName);
        Assert.False(enemy.IsAlive()); // and used the copy to win

        // ...and the swap reverted — the permanent move slot is Mimic again, not Tackle.
        Assert.Equal("mimic", player.MoveSet[0].Base.Name);
        Assert.Empty(player.Battle.MimicSwaps);
    }

    [Fact]
    public async Task TheCopiedMoveIsSelectableWhenMimicIsNotTheFirstSlot()
    {
        // Mimic swaps the slot's Base in place, so the copied move is still that slot.
        var player = TestCreatures.Make("Player", hp: 2000, attack: 200, speed: 200);
        player.AddAttack(Move("growl"));
        player.AddAttack(Move("mimic"));
        var enemy = TestCreatures.Make("Enemy", hp: 40, defense: 20, speed: 1);
        enemy.AddAttack(Move("tackle"));

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput("mimic", "tackle"),
            new ScriptedInput("tackle"),
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        var used = emitter
            .Of<MoveUsed>()
            .Where(e => e.AttackerName == "Player")
            .Select(e => e.MoveName)
            .ToList();
        Assert.Equal("mimic", used[0]);
        Assert.Equal("tackle", used[1]);
        Assert.DoesNotContain("growl", used);
    }

    private static Creature Fast(string name) =>
        TestCreatures.Make(name, hp: 2000, attack: 200, speed: 200);

    private static Creature SlowAndFrail(string name) =>
        TestCreatures.Make(name, hp: 40, defense: 20, speed: 1);

    [Fact]
    public async Task ThePlayerPicksWhichFoeMoveMimicCopiesAndTheCopySpendsMimicsPp()
    {
        var player = Fast("Player");
        player.AddAttack(Move("mimic"));
        var enemy = SlowAndFrail("Enemy");
        enemy.AddAttack(Move("growl"));
        enemy.AddAttack(Move("leer"));
        enemy.AddAttack(Move("tackle"));
        var playerInput = new ScriptedInput("mimic", "tackle").PicksMimic(2);

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            playerInput,
            new ScriptedInput("growl")
        );

        var offered = Assert.Single(playerInput.MimicChoicesOffered);
        Assert.Equal(["growl", "leer", "tackle"], offered.Moves.Select(m => m.Name));
        Assert.Equal(
            ["growl", "leer", "tackle"],
            Assert.Single(emitter.Of<MimicChoiceOffered>()).Moves
        );
        Assert.Equal("tackle", Assert.Single(emitter.Of<MimicLearned>()).MoveName);
        Assert.False(enemy.IsAlive());
        Assert.Equal(Move("mimic").PowerPointsMax - 2, player.MoveSet[0].PowerPointsCurrent);
    }

    [Theory]
    [InlineData(false, "tackle")]
    [InlineData(true, "leer")]
    public async Task TheFoesMimicCopiesARandomSlotWithoutAsking(bool highRoll, string copied)
    {
        var player = TestCreatures.Make("Player", hp: 2000, attack: 200, speed: 1);
        player.AddAttack(Move("tackle"));
        player.AddAttack(Move("growl"));
        player.AddAttack(Move("leer"));
        var enemy = TestCreatures.Make("Enemy", hp: 40, defense: 20, speed: 200);
        enemy.AddAttack(Move("mimic"));
        var enemyInput = new ScriptedInput("mimic");

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("tackle"),
            enemyInput,
            rng: highRoll ? new HighestRollSource() : new LowestRollSource()
        );

        Assert.Empty(enemyInput.MimicChoicesOffered);
        Assert.False(emitter.Of<MimicChoiceOffered>().Any());
        var learned = Assert.Single(emitter.Of<MimicLearned>());
        Assert.Equal("Enemy", learned.CreatureName);
        Assert.Equal(copied, learned.MoveName);
    }

    [Fact]
    public async Task MimicCanCopyMimicAndTheCopiedMimicCopiesAgain()
    {
        var player = Fast("Player");
        player.AddAttack(Move("mimic"));
        var enemy = SlowAndFrail("Enemy");
        enemy.AddAttack(Move("mimic"));
        enemy.AddAttack(Move("tackle"));

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("mimic", "mimic", "tackle").PicksMimic(0, 1),
            new ScriptedInput("tackle")
        );

        Assert.Equal(
            ["mimic", "tackle"],
            emitter
                .Of<MimicLearned>()
                .Where(e => e.CreatureName == "Player")
                .Select(e => e.MoveName)
        );
        Assert.False(emitter.Of<MoveFailed>().Any());
        Assert.False(enemy.IsAlive());
        Assert.Equal("mimic", player.MoveSet[0].Base.Name);
        Assert.Empty(player.Battle.MimicSwaps);
    }

    [Fact]
    public async Task AMimicCalledByMetronomeCopiesIntoTheMetronomeSlotBesideTheEarlierCopy()
    {
        var player = Fast("Player");
        player.AddAttack(Move("mimic"));
        player.AddAttack(Move("metronome"));
        var enemy = SlowAndFrail("Enemy");
        enemy.AddAttack(Move("growl"));
        enemy.AddAttack(Move("tackle"));

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("mimic", "metronome", "growl", "tackle").PicksMimic(1, 0),
            new ScriptedInput("growl"),
            movePool: [Move("mimic")]
        );

        Assert.Equal(
            ["tackle", "growl"],
            emitter
                .Of<MimicLearned>()
                .Where(e => e.CreatureName == "Player")
                .Select(e => e.MoveName)
        );
        Assert.Equal(
            ["mimic", "metronome", "mimic", "growl", "tackle"],
            emitter.Of<MoveUsed>().Where(e => e.AttackerName == "Player").Select(e => e.MoveName)
        );
        Assert.False(enemy.IsAlive());
        Assert.Equal(["mimic", "metronome"], player.MoveSet.Select(m => m.Base.Name));
        Assert.Equal(Move("metronome").PowerPointsMax - 2, player.MoveSet[1].PowerPointsCurrent);
        Assert.Empty(player.Battle.MimicSwaps);
    }

    [Fact]
    public async Task AMirrorMovedMimicCopiesIntoTheMirrorMoveSlot()
    {
        var player = TestCreatures.Make("Player", hp: 2000, attack: 200, speed: 1);
        player.AddAttack(Move("mirror-move"));
        var enemy = TestCreatures.Make("Enemy", hp: 40, defense: 20, speed: 200);
        enemy.AddAttack(Move("mimic"));
        enemy.AddAttack(Move("tackle"));

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("mirror-move", "tackle").PicksMimic(1),
            new ScriptedInput("mimic", "tackle"),
            rng: new LowestRollSource()
        );

        Assert.Equal(
            "tackle",
            Assert.Single(emitter.Of<MimicLearned>(), e => e.CreatureName == "Player").MoveName
        );
        Assert.Equal(
            ["mirror-move", "mimic", "tackle"],
            emitter
                .Of<MoveUsed>()
                .Where(e => e.AttackerName == "Player")
                .Select(e => e.MoveName)
                .Take(3)
        );
        Assert.False(enemy.IsAlive());
        Assert.Equal("mirror-move", player.MoveSet[0].Base.Name);
        Assert.Equal(Move("mirror-move").PowerPointsMax - 2, player.MoveSet[0].PowerPointsCurrent);
    }

    [Fact]
    public async Task TheFoesMirrorMovedMimicCopiesIntoItsMirrorMoveSlot()
    {
        var player = Fast("Player");
        player.AddAttack(Move("mimic"));
        player.AddAttack(Move("growl"));
        player.AddAttack(Move("tackle"));
        var enemy = SlowAndFrail("Enemy");
        enemy.AddAttack(Move("mirror-move"));

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("mimic", "growl", "tackle").PicksMimic(0),
            new ScriptedInput("mirror-move", "tackle"),
            rng: new HighestRollSource()
        );

        Assert.Equal(
            "tackle",
            Assert.Single(emitter.Of<MimicLearned>(), e => e.CreatureName == "Enemy").MoveName
        );
        Assert.Equal(
            ["mirror-move", "mimic", "tackle"],
            emitter.Of<MoveUsed>().Where(e => e.AttackerName == "Enemy").Select(e => e.MoveName)
        );
        Assert.False(enemy.IsAlive());
        Assert.Equal("mirror-move", enemy.MoveSet[0].Base.Name);
        Assert.Equal(Move("mirror-move").PowerPointsMax - 2, enemy.MoveSet[0].PowerPointsCurrent);
    }

    [Fact]
    public async Task AMimicCalledThroughMetronomeThenMirrorMoveCopiesIntoTheMetronomeSlot()
    {
        var player = TestCreatures.Make("Player", hp: 2000, attack: 200, speed: 1);
        player.AddAttack(Move("metronome"));
        var enemy = TestCreatures.Make("Enemy", hp: 40, defense: 20, speed: 200);
        enemy.AddAttack(Move("mimic"));
        enemy.AddAttack(Move("tackle"));

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("metronome", "tackle").PicksMimic(1),
            new ScriptedInput("mimic", "tackle"),
            rng: new LowestRollSource(),
            movePool: [Move("mirror-move")]
        );

        Assert.Equal(
            "tackle",
            Assert.Single(emitter.Of<MimicLearned>(), e => e.CreatureName == "Player").MoveName
        );
        Assert.Equal(
            ["metronome", "mirror-move", "mimic", "tackle"],
            emitter
                .Of<MoveUsed>()
                .Where(e => e.AttackerName == "Player")
                .Select(e => e.MoveName)
                .Take(4)
        );
        Assert.False(enemy.IsAlive());
        Assert.Equal("metronome", player.MoveSet[0].Base.Name);
        Assert.Equal(Move("metronome").PowerPointsMax - 2, player.MoveSet[0].PowerPointsCurrent);
    }

    [Fact]
    public async Task ASlotCopiedTwiceRevertsToItsFirstMove()
    {
        var player = Fast("Player");
        player.AddAttack(Move("mimic"));
        var enemy = SlowAndFrail("Enemy");
        enemy.AddAttack(Move("metronome"));
        enemy.AddAttack(Move("tackle"));

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("mimic", "metronome", "tackle").PicksMimic(0, 1),
            new ScriptedInput("tackle"),
            movePool: [Move("mimic")]
        );

        Assert.Equal(
            ["metronome", "tackle"],
            emitter
                .Of<MimicLearned>()
                .Where(e => e.CreatureName == "Player")
                .Select(e => e.MoveName)
        );
        Assert.Equal(
            ["mimic", "metronome", "mimic", "tackle"],
            emitter.Of<MoveUsed>().Where(e => e.AttackerName == "Player").Select(e => e.MoveName)
        );
        Assert.False(enemy.IsAlive());
        Assert.Equal("mimic", player.MoveSet[0].Base.Name);
        Assert.Empty(player.Battle.MimicSwaps);
    }

    [Fact]
    public async Task EveryMimicCopyRevertsAcrossASwitch()
    {
        var lead = Fast("Lead");
        lead.AddAttack(Move("mimic"));
        lead.AddAttack(Move("metronome"));
        lead.AddAttack(Move("tackle"));
        var bench = TestCreatures.Make("Bench", hp: 2000, speed: 200);
        bench.AddAttack(Move("tackle"));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make("Enemy", hp: 200, defense: 20, speed: 1);
        enemy.AddAttack(Move("growl"));
        enemy.AddAttack(Move("leer"));
        var playerInput = new ScriptedInput("mimic", "metronome", "mimic", "metronome", "tackle")
            .PicksMimic(0, 1, 0, 1)
            .TurnPlan(null, null, 1, 0);

        var emitter = await FullBattle.Fight(
            lead,
            enemy,
            playerInput,
            new ScriptedInput("growl"),
            movePool: [Move("mimic")],
            playerParty: party
        );

        Assert.Equal(
            ["growl", "leer", "growl", "leer"],
            emitter.Of<MimicLearned>().Where(e => e.CreatureName == "Lead").Select(e => e.MoveName)
        );
        Assert.False(enemy.IsAlive());
        Assert.Equal(["mimic", "metronome", "tackle"], lead.MoveSet.Select(m => m.Base.Name));
        Assert.Empty(lead.Battle.MimicSwaps);
    }

    [Fact]
    public async Task AMimicAfterTransformNeverLeaksTheEarlierCopyIntoThePermanentMoveSet()
    {
        var player = Fast("Player");
        player.AddAttack(Move("mimic"));
        player.AddAttack(Move("transform"));
        var enemy = SlowAndFrail("Enemy");
        enemy.AddAttack(Move("mimic"));
        enemy.AddAttack(Move("tackle"));

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("mimic", "transform", "mimic", "tackle").PicksMimic(1, 1),
            new ScriptedInput("tackle")
        );

        Assert.Equal(2, emitter.Of<MimicLearned>().Count(e => e.CreatureName == "Player"));
        Assert.False(enemy.IsAlive());
        Assert.Equal(["mimic", "transform"], player.MoveSet.Select(m => m.Base.Name));
        Assert.Empty(player.Battle.MimicSwaps);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public async Task AnOutOfRangeMimicPickCopiesTheFirstMove(int badPick)
    {
        var player = Fast("Player");
        player.AddAttack(Move("mimic"));
        var enemy = SlowAndFrail("Enemy");
        enemy.AddAttack(Move("tackle"));
        enemy.AddAttack(Move("growl"));

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("mimic", "tackle").PicksMimic(badPick),
            new ScriptedInput("growl")
        );

        Assert.Equal("tackle", Assert.Single(emitter.Of<MimicLearned>()).MoveName);
        Assert.False(enemy.IsAlive());
    }

    private sealed class RandomPickNoGrowlRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override int RollDamageVariance() => DamageVarianceDivisor;

        public override MimicPick MimicPickFor(BattleSide side) => MimicPick.RandomSlot;

        public override bool MimicCanCopy(Attack move) => move.Name != "growl";
    }

    [Fact]
    public async Task TheSeamDecidesWhoPicksAndWhatMimicCanCopy()
    {
        var player = Fast("Player");
        player.AddAttack(Move("mimic"));
        var enemy = SlowAndFrail("Enemy");
        enemy.AddAttack(Move("growl"));
        enemy.AddAttack(Move("tackle"));
        var playerInput = new ScriptedInput("mimic", "tackle");

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            playerInput,
            new ScriptedInput("growl"),
            new RandomPickNoGrowlRules(),
            new LowestRollSource()
        );

        Assert.Empty(playerInput.MimicChoicesOffered);
        Assert.False(emitter.Of<MimicChoiceOffered>().Any());
        Assert.Equal("tackle", Assert.Single(emitter.Of<MimicLearned>()).MoveName);
    }
}
