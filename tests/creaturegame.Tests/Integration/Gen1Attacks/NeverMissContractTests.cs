using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>Which moves skip the accuracy roll: Swift, and every move that never runs the hit test (GENERATION_SEAMS.md §2).</summary>
[Collection(MovesCollection.Name)]
public class NeverMissContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task SwiftHitsEvenWhenTheAccuracyRollAlwaysFails()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance) // every accuracy roll fails — a normal move would miss
            .Defender(TestCreatures.Make("Defender", hp: 500, defense: 80))
            .Use(Move("swift"));

        Assert.False(result.Has<MoveMissed>());
        Assert.True(result.Has<DamageDealt>());
        Assert.True(result.TotalDamage > 0);
    }

    // pokered core.asm:3124-3128 (ResidualEffects1) and :3144-3146 (any other 0-power move) skip MoveHitTest.
    [Theory]
    [InlineData("conversion")]
    [InlineData("haze")]
    [InlineData("mist")]
    [InlineData("focus-energy")]
    [InlineData("recover")]
    [InlineData("soft-boiled")]
    [InlineData("rest")]
    [InlineData("transform")]
    [InlineData("light-screen")]
    [InlineData("reflect")]
    [InlineData("substitute")]
    [InlineData("splash")]
    [InlineData("swords-dance")]
    [InlineData("growth")]
    [InlineData("agility")]
    [InlineData("harden")]
    [InlineData("amnesia")]
    [InlineData("minimize")]
    [InlineData("double-team")]
    [InlineData("withdraw")]
    [InlineData("defense-curl")]
    [InlineData("barrier")]
    [InlineData("acid-armor")]
    [InlineData("meditate")]
    [InlineData("sharpen")]
    [InlineData("roar")]
    [InlineData("whirlwind")]
    [InlineData("teleport")]
    public async Task AMoveThatNeverRunsTheHitTestDrawsNoAccuracyRollAndCantMiss(string move)
    {
        var rng = new RecordingRandomSource(new SeededRandomSource(0));
        var defender = TestCreatures.Make("Defender", hp: 500);
        defender.Battle.Stages.RaiseEvasion(6);

        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Rng(rng)
            .Defender(defender)
            .Use(Move(move));

        Assert.DoesNotContain(RecordingRandomSource.Accuracy, rng.Draws);
        Assert.False(result.Has<MoveMissed>());
    }

    // Bide's setup (ResidualEffects2), and a Metronome or Mirror Move that calls Swords Dance.
    [Theory]
    [InlineData("bide")]
    [InlineData("metronome")]
    [InlineData("mirror-move")]
    public async Task AMoveThatBranchesOffBeforeTheHitTestDrawsNoAccuracyRoll(string move)
    {
        var rng = new RecordingRandomSource(new SeededRandomSource(0));
        var defender = TestCreatures.Make("Defender", hp: 500);
        defender.Battle.Stages.RaiseEvasion(6);
        defender.Battle.LastMoveUsed = Move("swords-dance");

        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Rng(rng)
            .Defender(defender)
            .MovePool(Move("metronome"), Move("swords-dance"))
            .Use(Move(move));

        Assert.DoesNotContain(RecordingRandomSource.Accuracy, rng.Draws);
        Assert.False(result.Has<MoveMissed>());
    }

    // Each effect calls MoveHitTest itself: effects.asm :53, :108, :571, :1124, :1207, :1288; paralyze.asm:29; leech_seed.asm:2.
    [Theory]
    [InlineData("supersonic")]
    [InlineData("confuse-ray")]
    [InlineData("poison-powder")]
    [InlineData("poison-gas")]
    [InlineData("toxic")]
    [InlineData("thunder-wave")]
    [InlineData("stun-spore")]
    [InlineData("glare")]
    [InlineData("leech-seed")]
    [InlineData("mimic")]
    [InlineData("disable")]
    [InlineData("sleep-powder")]
    [InlineData("hypnosis")]
    [InlineData("growl")]
    [InlineData("screech")]
    [InlineData("sand-attack")]
    [InlineData("sing")]
    [InlineData("lovely-kiss")]
    [InlineData("spore")]
    [InlineData("leer")]
    [InlineData("tail-whip")]
    [InlineData("string-shot")]
    [InlineData("smokescreen")]
    [InlineData("kinesis")]
    [InlineData("flash")]
    [InlineData("counter")]
    public async Task AStatusMoveWhoseEffectRunsTheHitTestStillRollsAccuracy(string move)
    {
        var rng = new RecordingRandomSource(new SeededRandomSource(0));
        var defender = TestCreatures.Make("Defender", type1: DamageType.Water, hp: 500);
        defender.AddAttack(Move("tackle"));

        await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Rng(rng)
            .Defender(defender)
            .FoeLastMove(Move("tackle"), 20) // Counter reaches its roll only after a qualifying hit
            .Use(Move(move));

        Assert.Contains(RecordingRandomSource.Accuracy, rng.Draws);
    }

    // pokered core.asm:3124-3146 and :3205-3217: the residual effects that never call MoveHitTest, plus Metronome and Mirror Move.
    private static readonly HashSet<string> PokeredSkipsTheHitTest =
    [
        "conversion",
        "haze",
        "whirlwind",
        "roar",
        "teleport",
        "mist",
        "focus-energy",
        "recover",
        "soft-boiled",
        "rest",
        "transform",
        "light-screen",
        "reflect",
        "substitute",
        "splash",
        "swords-dance",
        "growth",
        "meditate",
        "sharpen",
        "harden",
        "withdraw",
        "defense-curl",
        "barrier",
        "acid-armor",
        "agility",
        "amnesia",
        "double-team",
        "minimize",
        "bide",
        "metronome",
        "mirror-move",
    ];

    [Fact]
    public void EveryImportedMoveRunsTheHitTestExactlyWhenPokeredDoes()
    {
        var wrong = AllMoves
            .Where(m =>
                Gen1BattleRules.Instance.RunsHitTest(m) == PokeredSkipsTheHitTest.Contains(m.Name!)
            )
            .Select(m => m.Name)
            .ToList();

        Assert.True(AllMoves.Count > 150);
        Assert.Subset(AllMoves.Select(m => m.Name!).ToHashSet(), PokeredSkipsTheHitTest);
        Assert.Empty(wrong);
    }

    [Fact]
    public async Task AnEvasiveFoeCantMakeRecoverOrSwordsDanceMiss()
    {
        var user = TestCreatures.Make("User", hp: 200);
        user.Attributes.HP = 50;
        var foe = TestCreatures.Make("Foe", hp: 500);
        foe.Battle.Stages.RaiseEvasion(6);
        var scenario = new MoveScenario()
            .Rules(Gen1BattleRules.Instance)
            .Rng(new HighestRollSource()) // a 255: the 1/256 miss, at any threshold
            .Attacker(user)
            .Defender(foe);

        var recover = await scenario.Use(Move("recover"));
        var swordsDance = await scenario.Use(Move("swords-dance"));

        Assert.Equal(100, Assert.Single(recover.Emitter.Of<Healed>()).HealAmount);
        Assert.False(swordsDance.Has<MoveMissed>());
        Assert.Equal(2, user.Battle.Stages.Attack);
    }

    [Fact]
    public async Task TheSeamDecidesWhetherASelfTargetingMoveRunsTheHitTest()
    {
        var result = await new MoveScenario()
            .Rules(new EveryMoveRunsTheHitTestRules())
            .Use(Move("swords-dance"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, result.Attacker.Battle.Stages.Attack);
    }

    private sealed class EveryMoveRunsTheHitTestRules : DelegatingBattleRules
    {
        public override bool RunsHitTest(Attack move) => true;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 0;
    }
}
