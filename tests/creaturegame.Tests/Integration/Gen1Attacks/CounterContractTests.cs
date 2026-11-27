using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>Counter's qualification, damage and move record; GENERATION_SEAMS.md §2 (Counter).</summary>
[Collection(MovesCollection.Name)]
public class CounterContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("tackle")]
    [InlineData("karate-chop")]
    public async Task CounterReturnsDoubleTheLastDamageAfterANormalOrFightingMove(string foeMove)
    {
        var result = await new MoveScenario()
            .FoeLastMove(Move(foeMove), 50)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        var hit = result.First<DamageDealt>();
        Assert.NotNull(hit);
        Assert.Equal(100, hit.Damage);
        Assert.Equal("D", hit.TargetName);
    }

    [Theory]
    [InlineData("water-gun")]
    [InlineData("dragon-rage")]
    [InlineData("night-shade")]
    [InlineData("psywave")]
    [InlineData("growl")]
    [InlineData("counter")]
    [InlineData("bide")]
    public async Task CounterMissesAfterAMoveItCannotAnswer(string foeMove)
    {
        var result = await new MoveScenario()
            .FoeLastMove(Move(foeMove), 50)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Theory]
    [InlineData("sonic-boom")]
    [InlineData("seismic-toss")]
    [InlineData("super-fang")]
    public async Task CounterAnswersFixedAndLevelBasedNormalOrFightingMoves(string foeMove)
    {
        // pokered gives these power 1, so they pass Counter's 0-power check.
        var result = await new MoveScenario()
            .FoeLastMove(Move(foeMove), 20)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        Assert.Equal(40, result.First<DamageDealt>()!.Damage);
    }

    [Fact]
    public async Task CounterMissesWhenNoDamageIsOnRecord()
    {
        var result = await new MoveScenario()
            .FoeLastMove(Move("tackle"), 0)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(false, 50)]
    public async Task ACounterThatMissesItsAccuracyRoll_ZeroesTheLastDamageUnlessTheSeamSaysNot(
        bool? missZeroes,
        int lastDamageAfter
    )
    {
        var scenario = new MoveScenario()
            .Rules(new ForcedMissRules(missZeroes))
            .FoeLastMove(Move("tackle"), 50)
            .Defender(TestCreatures.Make("D", hp: 500));

        var result = await scenario.Use(Move("counter"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(lastDamageAfter, scenario.LastDamage);
    }

    [Fact]
    public async Task ACounterThatFailsItsChecks_LeavesTheLastDamage()
    {
        var scenario = new MoveScenario()
            .Rules(new ForcedMissRules())
            .FoeLastMove(Move("water-gun"), 50)
            .Defender(TestCreatures.Make("D", hp: 500));

        var result = await scenario.Use(Move("counter"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(50, scenario.LastDamage);
    }

    [Fact]
    public void Gen1CounterDamageIsCappedAt65535()
    {
        Assert.Equal(65535, Gen1BattleRules.Instance.CounterDamage(40000));
        Assert.Equal(200, Gen1BattleRules.Instance.CounterDamage(100));
    }

    [Fact]
    public async Task TheSeamDecidesWhichMovesCounterAnswers()
    {
        var result = await new MoveScenario()
            .Rules(new AnswersAnyMoveRules())
            .FoeLastMove(Move("water-gun"), 50)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        Assert.Equal(100, result.First<DamageDealt>()!.Damage);
    }

    [Fact]
    public async Task TheSeamDecidesCountersDamage()
    {
        var result = await new MoveScenario()
            .Rules(new UndoubledCounterRules())
            .FoeLastMove(Move("tackle"), 50)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        Assert.Equal(50, result.First<DamageDealt>()!.Damage);
    }

    private sealed class AnswersAnyMoveRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override bool CounterQualifies(Attack? foeSelected, Attack? foeLoaded) => true;
    }

    private sealed class ForcedMissRules(bool? missZeroes = null) : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 0;

        public override bool MissZeroesLastDamage => missZeroes ?? base.MissZeroesLastDamage;
    }

    private sealed class UndoubledCounterRules : DelegatingBattleRules
    {
        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;

        public override int CounterDamage(int lastDamage) => lastDamage;
    }

    // ── Full battles ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CounterAnswersTheOpponentsPhysicalHitInAFullBattle()
    {
        var player = TestCreatures.Make("Player", hp: 9999, speed: 1);
        player.AddAttack(Move("counter"));
        var enemy = TestCreatures.Make("Enemy", hp: 400, speed: 200);
        enemy.AddAttack(Move("tackle"));

        var log = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("counter"),
            new ScriptedInput("tackle"),
            new CounterDuelRules()
        );

        Assert.Equal(2 * HitsOn(log, "Player")[0], HitsOn(log, "Enemy")[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CounterMissesWhenTheFoesNormalMoveMissed(bool playerCounters)
    {
        // Mega Kick (75%) misses under these rules, and pokered's miss zeroes the shared damage.
        var (user, foe) = Duel("tackle", "mega-kick");
        var userScript = new ScriptedInput("counter", "counter", "strength");
        var foeScript = new ScriptedInput("tackle", "mega-kick");

        var log = await FightAs(playerCounters, user, foe, userScript, foeScript);

        var counterOnFoe = HitsOn(log, foe.Name);
        Assert.Equal(2, counterOnFoe.Count);
        Assert.Equal(2 * HitsOn(log, user.Name)[0], counterOnFoe[0]);
        Assert.Single(log.Of<MoveMissed>(), m => m.AttackerName == user.Name);
        Assert.False(foe.IsAlive());
    }

    [Fact]
    public async Task CounterAnswersAHitItsSubstituteSoaked()
    {
        var (user, foe) = Duel("tackle", "strength");
        user.AddAttack(Move("substitute"));

        var log = await FightAs(
            true,
            user,
            foe,
            new ScriptedInput("substitute", "counter", "strength"),
            new ScriptedInput("tackle", "strength")
        );

        int decoy = log.Of<SubstituteCreated>().Single().SubstituteHp;
        int soaked = decoy - log.Of<SubstituteAbsorbedHit>().First().SubstituteHpAfter;
        Assert.Equal(2 * soaked, HitsOn(log, "Foe")[0]);
    }

    [Fact]
    public async Task CounterOnAFullyParalysedFoeDoublesTheSharedDamageAgain()
    {
        // The foe's loaded move is still Tackle, and the last damage is the user's own first Counter.
        var (user, foe) = Duel("tackle");
        user.AddAttack(Move("thunder-wave"));
        user.Attributes.Speed = 300;

        var log = await FightAs(
            true,
            user,
            foe,
            new ScriptedInput("thunder-wave", "counter", "counter", "strength"),
            new ScriptedInput("tackle"),
            new CounterDuelRules(fullParalysisFrom: 3)
        );

        var onFoe = HitsOn(log, "Foe");
        Assert.Equal(2 * HitsOn(log, "User")[1], onFoe[0]);
        Assert.Equal(2 * onFoe[0], onFoe[1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothSidesCounteringTheSameTurnBothMiss(bool playerIsFaster)
    {
        var fast = TestCreatures.Make(playerIsFaster ? "Player" : "Enemy", hp: 600, speed: 200);
        fast.AddAttack(Move("tackle"));
        fast.AddAttack(Move("counter"));
        var slow = TestCreatures.Make(
            playerIsFaster ? "Enemy" : "Player",
            hp: 500,
            attack: 999,
            speed: 1
        );
        slow.AddAttack(Move("counter"));
        slow.AddAttack(Move("strength"));
        var fastScript = new ScriptedInput("tackle", "counter", "tackle");
        var slowScript = new ScriptedInput("counter", "counter", "strength");

        var log = playerIsFaster
            ? await FullBattle.Fight(fast, slow, fastScript, slowScript, new CounterDuelRules())
            : await FullBattle.Fight(slow, fast, slowScript, fastScript, new CounterDuelRules());

        var turnTwoMisses = log.Of<MoveMissed>().Where(m => m.MoveName == "counter").ToList();
        Assert.Equal(2, turnTwoMisses.Count);
        Assert.Contains(turnTwoMisses, m => m.AttackerName == fast.Name);
        Assert.Contains(turnTwoMisses, m => m.AttackerName == slow.Name);
    }

    [Fact]
    public async Task AnEnemyCounterOnASwitchTurnHitsTheIncomingCreature()
    {
        // The player's record keeps the lead's Tackle, and the last damage is the enemy's own Counter.
        var lead = TestCreatures.Make("Lead", hp: 9999, speed: 300);
        lead.AddAttack(Move("tackle"));
        var bench = TestCreatures.Make("Bench", hp: 9999, attack: 999, speed: 300);
        bench.AddAttack(Move("strength"));
        var party = new Party(lead);
        party.Add(bench);
        var enemy = TestCreatures.Make("Enemy", hp: 500, speed: 1);
        enemy.AddAttack(Move("counter"));

        var log = await FullBattle.Fight(
            lead,
            enemy,
            new ScriptedInput("tackle", "strength").TurnPlan(null, 1),
            new ScriptedInput("counter"),
            new CounterDuelRules(),
            playerParty: party
        );

        Assert.Equal(2 * HitsOn(log, "Lead")[0], HitsOn(log, "Bench")[0]);
    }

    [Fact]
    public async Task AnEnemyCounterOnAnItemTurnReadsThePlayersPreviousSelection()
    {
        var player = TestCreatures.Make("Player", hp: 9999, attack: 999, speed: 300);
        player.Attributes.HP = 5000;
        player.AddAttack(Move("tackle"));
        player.AddAttack(Move("strength"));
        var enemy = TestCreatures.Make("Enemy", hp: 500, speed: 1);
        enemy.AddAttack(Move("counter"));
        var bag = new Bag();
        bag.Add(17);

        var log = await FullBattle.Fight(
            player,
            enemy,
            new TackleThenPotionInput(),
            new ScriptedInput("counter"),
            new CounterDuelRules(),
            playerBag: bag
        );

        var onPlayer = HitsOn(log, "Player");
        Assert.Single(log.Of<ItemUsed>());
        Assert.Equal(2 * onPlayer[0], onPlayer[1]);
    }

    [Fact]
    public async Task CounterAnswersTheFoesConfusionSelfHit()
    {
        var (user, foe) = Duel("tackle");
        user.AddAttack(Move("confuse-ray"));

        var log = await FightAs(
            true,
            user,
            foe,
            new ScriptedInput("confuse-ray", "counter", "strength"),
            new ScriptedInput("tackle"),
            new CounterDuelRules { SelfHitPercent = 100, ConfusionTurns = 5 }
        );

        Assert.Equal(2 * log.Of<ConfusionDamage>().First().Damage, HitsOn(log, "Foe")[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSeamDecidesWhetherAnAsleepEnemysPickRewritesItsSelection(bool skips)
    {
        // The asleep enemy "picks" Counter; skipped, its record keeps the Tackle it last used.
        var player = TestCreatures.Make("Player", hp: 9999, attack: 999, speed: 1);
        player.AddAttack(Move("hypnosis"));
        player.AddAttack(Move("tackle"));
        player.AddAttack(Move("counter"));
        player.AddAttack(Move("strength"));
        var enemy = TestCreatures.Make("Enemy", hp: 300, speed: 200);
        enemy.AddAttack(Move("tackle"));
        enemy.AddAttack(Move("counter"));

        var log = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("hypnosis", "tackle", "counter", "strength"),
            new ScriptedInput("tackle", "counter"),
            new CounterDuelRules
            {
                AlwaysHits = true,
                SleepTurns = 3,
                SkipsSelection = skips,
            }
        );

        var onEnemy = HitsOn(log, "Enemy");
        Assert.Equal(skips, onEnemy.Count > 1 && onEnemy[1] == 2 * onEnemy[0]);
        Assert.Equal(!skips, log.Of<MoveMissed>().Any(m => m.AttackerName == "Player"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSeamDecidesWhetherAnAsleepPlayersPickRewritesItsSelection(bool skips)
    {
        var player = TestCreatures.Make("Player", hp: 600, speed: 300);
        player.AddAttack(Move("tackle"));
        player.AddAttack(Move("counter"));
        var enemy = TestCreatures.Make("Enemy", hp: 9999, attack: 999, speed: 1);
        enemy.AddAttack(Move("hypnosis"));
        enemy.AddAttack(Move("tackle"));
        enemy.AddAttack(Move("counter"));

        var log = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("tackle", "counter"),
            new ScriptedInput("hypnosis", "tackle", "counter"),
            new CounterDuelRules
            {
                AlwaysHits = true,
                SleepTurns = 3,
                SkipsSelection = skips,
            }
        );

        var onPlayer = HitsOn(log, "Player");
        Assert.Equal(skips, onPlayer.Count > 1 && onPlayer[1] == 2 * onPlayer[0]);
        Assert.Equal(!skips, log.Of<MoveMissed>().Any(m => m.AttackerName == "Enemy"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSeamDecidesWhetherAFrozenEnemysPickRewritesItsSelection(bool skips)
    {
        // The forced-secondary Ice Beam freezes the enemy, which then "picks" Counter.
        var (user, foe) = Duel("tackle", "counter");
        user.AddAttack(Move("ice-beam"));

        var log = await FightAs(
            true,
            user,
            foe,
            new ScriptedInput("ice-beam", "counter", "strength"),
            new ScriptedInput("tackle", "counter"),
            new CounterDuelRules { ForcesSecondary = true, SkipsSelection = skips }
        );

        var onFoe = HitsOn(log, "Foe");
        Assert.Equal(skips, onFoe[1] == 2 * onFoe[0]);
        Assert.Equal(!skips, log.Of<MoveMissed>().Any(m => m.AttackerName == "User"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheSeamDecidesWhetherARechargingEnemysPickRewritesItsSelection(bool skips)
    {
        var (user, foe) = Duel("hyper-beam", "counter");
        foe.Attributes.MaxHP = foe.Attributes.HP = 700;

        var log = await FightAs(
            true,
            user,
            foe,
            new ScriptedInput("counter", "counter", "strength"),
            new ScriptedInput("hyper-beam", "counter"),
            new CounterDuelRules { AlwaysHits = true, SkipsSelection = skips }
        );

        var onFoe = HitsOn(log, "Foe");
        Assert.Equal(skips, onFoe[1] == 2 * onFoe[0]);
        Assert.Equal(!skips, log.Of<MoveMissed>().Any(m => m.AttackerName == "User"));
    }

    [Fact]
    public async Task ACalledCounterBecomesTheCallersSelection()
    {
        // The player's Metronome calls Counter, then sleep keeps that selection; the enemy's later Counter misses on it.
        var player = TestCreatures.Make("Player", hp: 300, speed: 300);
        player.AddAttack(Move("metronome"));
        var enemy = TestCreatures.Make("Enemy", hp: 9999, attack: 999, speed: 1);
        enemy.AddAttack(Move("hypnosis"));
        enemy.AddAttack(Move("tackle"));
        enemy.AddAttack(Move("counter"));
        enemy.AddAttack(Move("strength"));

        var log = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("metronome"),
            new ScriptedInput("hypnosis", "tackle", "counter", "strength"),
            new CounterDuelRules { AlwaysHits = true, SleepTurns = 3 },
            movePool: [Move("counter")]
        );

        Assert.Contains(
            log.Of<MoveMissed>(),
            m => m.AttackerName == "Enemy" && m.MoveName == "counter"
        );
        var onPlayer = HitsOn(log, "Player");
        Assert.DoesNotContain(onPlayer.Skip(1), d => d == 2 * onPlayer[0]);
    }

    [Fact]
    public async Task AStrugglingPlayerKeepsItsLastLoadedMoveUntilStruggleRuns()
    {
        // Out of PP and fully paralysed, the player's loaded move is still Growl, so the enemy's Counter misses.
        var player = TestCreatures.Make("Player", type1: DamageType.Water, hp: 600, speed: 300);
        player.AddAttack(Move("growl"));
        player.MoveSet[0].PowerPointsCurrent = 1;
        var enemy = TestCreatures.Make("Enemy", hp: 9999, speed: 1);
        enemy.AddAttack(Move("body-slam"));
        enemy.AddAttack(Move("counter"));
        enemy.AddAttack(Move("strength"));

        var log = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("growl"),
            new ScriptedInput("body-slam", "counter", "strength"),
            new CounterDuelRules(fullParalysisFrom: 1) { AlwaysHits = true, ForcesSecondary = true }
        );

        Assert.Single(log.Of<MoveMissed>(), m => m.AttackerName == "Enemy");
        Assert.DoesNotContain(log.Of<DamageDealt>(), d => d.TargetName == "Enemy");
    }

    // pokered data/moves/moves.asm: every move with power 0.
    private static readonly HashSet<string> PokeredZeroPower =
    [
        "swords-dance",
        "whirlwind",
        "sand-attack",
        "tail-whip",
        "leer",
        "growl",
        "roar",
        "sing",
        "supersonic",
        "disable",
        "mist",
        "leech-seed",
        "growth",
        "poison-powder",
        "stun-spore",
        "sleep-powder",
        "string-shot",
        "thunder-wave",
        "toxic",
        "hypnosis",
        "meditate",
        "agility",
        "teleport",
        "night-shade",
        "mimic",
        "screech",
        "double-team",
        "recover",
        "harden",
        "minimize",
        "smokescreen",
        "confuse-ray",
        "withdraw",
        "defense-curl",
        "barrier",
        "light-screen",
        "haze",
        "reflect",
        "focus-energy",
        "bide",
        "metronome",
        "mirror-move",
        "amnesia",
        "kinesis",
        "soft-boiled",
        "glare",
        "poison-gas",
        "lovely-kiss",
        "transform",
        "spore",
        "flash",
        "splash",
        "acid-armor",
        "rest",
        "sharpen",
        "conversion",
        "substitute",
    ];

    [Fact]
    public void CounterAnswersExactlyTheImportedNormalAndFightingMovesPokeredGivesPower()
    {
        var physical = AllMoves
            .Where(m => m.DamageType is DamageType.Normal or DamageType.Fighting)
            .ToList();
        var wrong = physical
            .Where(m =>
                Gen1BattleRules.Instance.CounterQualifies(null, m)
                == PokeredZeroPower.Contains(m.Name!)
            )
            .Select(m => m.Name)
            .ToList();

        Assert.True(physical.Count > 40);
        Assert.Empty(wrong);
    }

    private sealed class TackleThenPotionInput : IBattleInput
    {
        private int _turn;

        private static PokemonAttack Named(TurnContext c, string name) =>
            c.Attacker.MoveSet.First(m => m.Base.Name == name);

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            Task.FromResult(Named(context, "strength"));

        public Task<TurnChoice> ChooseTurnActionAsync(TurnContext context) =>
            Task.FromResult<TurnChoice>(
                ++_turn switch
                {
                    1 => new MoveTurnChoice(Named(context, "tackle")),
                    2 => new ItemTurnChoice(
                        new Item
                        {
                            Id = 17,
                            Name = "potion",
                            Category = ItemCategory.Healing,
                            HealAmount = 20,
                        }
                    ),
                    _ => new MoveTurnChoice(Named(context, "strength")),
                }
            );
    }

    // The Counter user is slow and bulky; its strong Strength ends the battle once the case has played out.
    private (Creature User, Creature Foe) Duel(params string[] foeMoves)
    {
        var user = TestCreatures.Make("User", hp: 9999, attack: 999, speed: 1);
        user.AddAttack(Move("counter"));
        user.AddAttack(Move("strength"));
        var foe = TestCreatures.Make("Foe", hp: 300, speed: 200);
        foreach (var move in foeMoves)
            foe.AddAttack(Move(move));
        return (user, foe);
    }

    private static Task<RecordingEmitter> FightAs(
        bool playerCounters,
        Creature user,
        Creature foe,
        ScriptedInput userScript,
        ScriptedInput foeScript,
        IBattleRules? rules = null
    ) =>
        playerCounters
            ? FullBattle.Fight(user, foe, userScript, foeScript, rules ?? new CounterDuelRules())
            : FullBattle.Fight(foe, user, foeScript, userScript, rules ?? new CounterDuelRules());

    private static List<int> HitsOn(RecordingEmitter log, string name) =>
        log.Of<DamageDealt>().Where(d => d.TargetName == name).Select(d => d.Damage).ToList();

    // Unless AlwaysHits, moves under 85% accuracy miss (Mega Kick) and the rest hit (Tackle's 95); no crit or variance.
    private sealed class CounterDuelRules(int fullParalysisFrom = int.MaxValue)
        : DelegatingBattleRules
    {
        private int _paralysisChecks;

        public bool AlwaysHits { get; init; }
        public bool ForcesSecondary { get; init; }
        public int? SleepTurns { get; init; }
        public int? SelfHitPercent { get; init; }
        public int? ConfusionTurns { get; init; }
        public bool? SkipsSelection { get; init; }

        public override int GetHitThreshold(int acc, int accStage, int evaStage) =>
            AlwaysHits || acc >= 85 ? 256 : 0;

        public override double GetCritChance(Creature a, Attack m) => 0;

        public override int RollDamageVariance() => DamageVarianceDivisor;

        public override bool RollsFullParalysis(IRandomSource rng) =>
            ++_paralysisChecks >= fullParalysisFrom;

        public override int GetSecondaryEffectChance(Attack m, SecondaryEffectKind e) =>
            ForcesSecondary ? 100 : base.GetSecondaryEffectChance(m, e);

        public override int RollSleepTurns() => SleepTurns ?? base.RollSleepTurns();

        public override int ConfusionSelfHitPercent =>
            SelfHitPercent ?? base.ConfusionSelfHitPercent;

        public override int RollConfusionTurns() => ConfusionTurns ?? base.RollConfusionTurns();

        public override bool SkipsMoveSelection(Creature creature) =>
            SkipsSelection != false && base.SkipsMoveSelection(creature);
    }
}
