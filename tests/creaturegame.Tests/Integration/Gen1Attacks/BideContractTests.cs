using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>Bide stores, then unleashes double; what it stores is <see cref="IBattleRules.BideAccumulation"/> (GENERATION_SEAMS.md §2).</summary>
[Collection(MovesCollection.Name)]
public class BideContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task BideStoresWithoutAttackingOnTheFirstTurn()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("bide"));

        Assert.True(result.Has<BideStoring>());
        Assert.False(result.Has<DamageDealt>(), "Bide deals no damage while storing");
        Assert.True(result.Attacker.Battle.BideTurnsRemaining > 0, "the user is committed to Bide");
    }

    [Fact]
    public async Task BideUnleashesDoubleTheDamageAbsorbed()
    {
        // 2-turn Bide: store turn 1 (absorbing the enemy's hit), unleash turn 2. The player outspeeds,
        // so on the release turn it unleashes before the enemy acts — felling the 1-HP enemy. The
        // released damage must equal 2× the total the player absorbed while storing.
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 2000;
        player.Attributes.Speed = 200;
        player.AddAttack(Move("bide"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 1; // any non-zero unleash fells it
        enemy.Attributes.Attack = 60;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(Move("tackle"));

        var emitter = new RecordingEmitter();
        var input = new CountingInput(0);
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            input,
            AutoSelectInput.Instance,
            rules: new FixedBideRules(),
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        Assert.Contains(emitter.Events, e => e is BideStoring);
        Assert.Equal(2, input.CallCount); // asked every turn — the menu shows under Bide; FIGHT continues it
        Assert.False(enemy.IsAlive());

        int absorbed = emitter
            .Of<DamageDealt>()
            .Where(d => d.TargetName == "Player")
            .Sum(d => d.Damage);
        var unleashed = emitter.Of<DamageDealt>().First(d => d.TargetName == "Enemy");
        Assert.True(absorbed > 0, "the player absorbed a hit while storing");
        Assert.Equal(absorbed * 2, unleashed.Damage);
    }

    [Fact]
    public async Task BideAbsorbsNonStandardDamageToo()
    {
        // Bide stores damage from any category, not just normal attacks. The enemy uses Sonic Boom
        // (fixed 20) while the player stores; the release must be exactly 2 × 20 = 40.
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 2000;
        player.Attributes.Speed = 200;
        player.AddAttack(Move("bide"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(Move("sonic-boom")); // fixed 20 damage (DamageCategory.Fixed)

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            new CountingInput(0),
            AutoSelectInput.Instance,
            rules: new FixedBideRules(),
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        var unleashed = emitter.Of<DamageDealt>().First(d => d.TargetName == "Enemy");
        Assert.Equal(40, unleashed.Damage); // 2 × the fixed 20 absorbed while storing
    }

    [Fact]
    public async Task AFoeMoveThatSkipsTheDamageCalc_CountsThePreviousHitAgain()
    {
        var (emitter, unleashed) = await BideAgainstAsync(
            Foe("tackle", "splash"),
            new FixedBideRules(3),
            ["tackle", "splash"]
        );

        Assert.Equal(4 * HitsOn(emitter, "Player").Single(), unleashed);
    }

    [Fact]
    public async Task AFoeThatCantMove_CountsThePreviousHitAgain()
    {
        var (emitter, unleashed) = await BideAgainstAsync(
            Foe("hyper-beam"),
            new FixedBideRules(3),
            ["hyper-beam"]
        );

        Assert.Contains(emitter.Events, e => e is Recharging { CreatureName: "Enemy" });
        Assert.Equal(4 * HitsOn(emitter, "Player").Single(), unleashed);
    }

    [Fact]
    public async Task AMultiHitMove_CountsOneHitPerTurn()
    {
        var (emitter, unleashed) = await BideAgainstAsync(
            Foe("double-kick"),
            new FixedBideRules(3),
            ["double-kick"]
        );

        var hits = HitsOn(emitter, "Player");
        Assert.Equal(4, hits.Count);
        Assert.Equal(4 * hits[0], unleashed);
    }

    [Fact]
    public async Task AHitOnTheBideUsersSubstitute_IsStored()
    {
        var (emitter, unleashed) = await BideAgainstAsync(
            Foe("tackle"),
            new FixedBideRules(3),
            ["tackle"],
            ["substitute", "bide"]
        );

        var struck = emitter.Of<SubstituteAbsorbedHit>().ToList();
        int hit = struck[0].SubstituteHpAfter - struck[1].SubstituteHpAfter;
        Assert.Empty(HitsOn(emitter, "Player"));
        Assert.Equal(4 * hit, unleashed);
    }

    [Fact]
    public async Task AFoesConfusionSelfHit_IsStored()
    {
        var enemy = Foe("tackle");
        var rules = new FixedBideRules(3, selfHitPercent: 100, confusionTurns: 5);

        var (emitter, unleashed) = await BideAgainstAsync(
            enemy,
            rules,
            ["tackle"],
            ["confuse-ray", "bide"],
            player =>
                enemy.Attributes.HP = enemy.Attributes.MaxHP =
                    4 * DamageCalculator.CalculateConfusionDamage(enemy, player, rules)
        );

        Assert.Equal(4 * emitter.Of<ConfusionDamage>().First().Damage, unleashed);
    }

    [Fact]
    public async Task AFoeMoveThatZeroesTheLastDamage_StoresNothingThatTurn()
    {
        var (emitter, unleashed) = await BideAgainstAsync(
            Foe("tackle", "growl"),
            new FixedBideRules(3),
            ["tackle", "growl"]
        );

        Assert.Equal(2 * HitsOn(emitter, "Player").Single(), unleashed);
    }

    [Fact]
    public async Task AFoesFailedCounter_CountsThePreviousHitAgain()
    {
        var (emitter, unleashed) = await BideAgainstAsync(
            Foe("tackle", "counter"),
            new FixedBideRules(3),
            ["tackle", "counter"]
        );

        Assert.Equal(4 * HitsOn(emitter, "Player").Single(), unleashed);
    }

    [Fact]
    public async Task AFoesCrash_IsStored()
    {
        var enemy = Foe("jump-kick");
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 3;

        var (emitter, unleashed) = await BideAgainstAsync(
            enemy,
            new FixedBideRules(3),
            ["jump-kick"],
            setUp: player => player.Type1 = DamageType.Ghost
        );

        Assert.Equal(4 * emitter.Of<CrashDamage>().First().Damage, unleashed);
    }

    [Fact]
    public async Task AFoesMetronomeCalledHit_IsStored()
    {
        var (emitter, unleashed) = await BideAgainstAsync(
            Foe("metronome"),
            new FixedBideRules(3),
            ["metronome"],
            movePool: [Move("tackle")]
        );

        Assert.Equal(4 * HitsOn(emitter, "Player")[0], unleashed);
    }

    [Theory]
    [InlineData("conversion", true)]
    [InlineData("haze", true)]
    [InlineData("whirlwind", true)]
    [InlineData("roar", true)]
    [InlineData("teleport", true)]
    [InlineData("mist", true)]
    [InlineData("focus-energy", true)]
    [InlineData("confuse-ray", true)]
    [InlineData("supersonic", true)]
    [InlineData("recover", true)]
    [InlineData("soft-boiled", true)]
    [InlineData("rest", true)]
    [InlineData("transform", true)]
    [InlineData("light-screen", true)]
    [InlineData("reflect", true)]
    [InlineData("toxic", true)]
    [InlineData("poison-powder", true)]
    [InlineData("poison-gas", true)]
    [InlineData("thunder-wave", true)]
    [InlineData("stun-spore", true)]
    [InlineData("glare", true)]
    [InlineData("substitute", true)]
    [InlineData("mimic", true)]
    [InlineData("leech-seed", true)]
    [InlineData("splash", true)]
    [InlineData("counter", true)]
    [InlineData("sleep-powder", false)]
    [InlineData("growl", false)]
    [InlineData("swords-dance", false)]
    [InlineData("disable", false)]
    [InlineData("metronome", false)]
    [InlineData("bide", false)]
    [InlineData("tackle", false)]
    [InlineData("seismic-toss", false)]
    [InlineData("poison-sting", false)]
    [InlineData("body-slam", false)]
    public void Gen1SkipsTheDamageCalcForResidualEffects1AndCounter(string move, bool skips) =>
        Assert.Equal(skips, Gen1BattleRules.Instance.SkipsDamageCalculation(Move(move)));

    [Fact]
    public async Task UnderRulesThatStoreEachHit_AFoeMoveThatSkipsTheDamageCalcAddsNothing()
    {
        var (emitter, unleashed) = await BideAgainstAsync(
            Foe("tackle", "splash"),
            new FixedBideRules(3, accumulation: BideAccumulation.EachHitTaken),
            ["tackle", "splash"]
        );

        Assert.Equal(2 * HitsOn(emitter, "Player").Single(), unleashed);
    }

    // A faster player plays playerMoves (Bide last: store, store, release) at a foe playing foeMoves; returns the unleash.
    private async Task<(RecordingEmitter Emitter, int Unleashed)> BideAgainstAsync(
        Creature enemy,
        IBattleRules rules,
        string[] foeMoves,
        string[]? playerMoves = null,
        Action<Creature>? setUp = null,
        IReadOnlyList<Attack>? movePool = null
    )
    {
        playerMoves ??= ["bide"];
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 2000;
        player.Attributes.Speed = 200;
        foreach (var move in playerMoves)
            player.AddAttack(Move(move));
        setUp?.Invoke(player);

        var emitter = new RecordingEmitter();
        await new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput(playerMoves),
            new ScriptedInput(foeMoves),
            rules: rules,
            emitter: emitter,
            rng: new SeededRandomSource(1),
            movePool: movePool
        ).StartFightAsync();

        return (emitter, emitter.Of<DamageDealt>().First(d => d.TargetName == "Enemy").Damage);
    }

    private Creature Foe(params string[] moves)
    {
        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Attack = 60;
        enemy.Attributes.Speed = 1;
        foreach (var move in moves)
            enemy.AddAttack(Move(move));
        return enemy;
    }

    private static List<int> HitsOn(RecordingEmitter emitter, string target) =>
        emitter.Of<DamageDealt>().Where(d => d.TargetName == target).Select(d => d.Damage).ToList();

    /// <summary>Always-hit, no-crit, no-variance, and a fixed Bide length for determinism.</summary>
    private sealed class FixedBideRules(
        int bideTurns = 2,
        int? selfHitPercent = null,
        BideAccumulation? accumulation = null,
        int? confusionTurns = null
    ) : DelegatingBattleRules
    {
        public override int RollBideTurns() => bideTurns;

        public override int RollConfusionTurns() => confusionTurns ?? base.RollConfusionTurns();

        public override int ConfusionSelfHitPercent =>
            selfHitPercent ?? base.ConfusionSelfHitPercent;

        public override BideAccumulation BideAccumulation => accumulation ?? base.BideAccumulation;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override double RollDamageVariance() => 1.0;
    }

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
