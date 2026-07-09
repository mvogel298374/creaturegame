using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// Node-kind dispatch in <see cref="RunDirector"/>: a biome's route is a plan of <see cref="RunNodeKind"/>
/// nodes the director dispatches — three battle tiers (mapped to a generation-agnostic <see cref="EncounterTier"/>
/// the supplier consumes) and three interaction nodes (Shop/Treasure/Mystery: emit a banner, advance the biome,
/// and — for Shop/Treasure/Mystery — run their economy behaviour). The default layout caps each biome with a
/// Boss apex. Enemies are supplied by a delegate (no DB), so these pin sequencing + the tier/banner wiring, not
/// encounter contents.
/// </summary>
public class RunDirectorNodeTests
{
    // --- The default biome route layout (battle-heavy tuned curve, Boss-capped — 3c-2) ---------------------

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void DefaultNodePlan_IsBossCapped_AtThePlannedLength(int length)
    {
        var plan = RunDirector.DefaultNodePlan(length, new SeededRandomSource(0));

        Assert.Equal(length, plan.Count);
        Assert.Equal(RunNodeKind.BossBattle, plan[^1]); // the biome apex (ENCOUNTER_DESIGN.md §4)
    }

    [Fact]
    public void DefaultNodePlan_LengthOne_IsJustTheBoss()
    {
        Assert.Equal(
            new[] { RunNodeKind.BossBattle },
            RunDirector.DefaultNodePlan(1, new SeededRandomSource(3)).ToArray()
        );
    }

    [Fact]
    public void DefaultNodePlan_IsReproducibleFromSeed()
    {
        var a = RunDirector.DefaultNodePlan(6, new SeededRandomSource(99));
        var b = RunDirector.DefaultNodePlan(6, new SeededRandomSource(99));

        Assert.Equal(a.ToArray(), b.ToArray()); // same seed → same interior mix + order
    }

    [Fact]
    public void DefaultNodePlan_InteriorIsBattleHeavy_AndCoversEveryKind()
    {
        // Sample many interior slots (every node but the Boss apex) from one seeded stream and tally the mix.
        // Pins the 3c-2 distribution's *shape* without coupling to exact percentages: wild battles dominate,
        // and every non-boss kind is reachable.
        var rng = new SeededRandomSource(2024);
        var tally = new Dictionary<RunNodeKind, int>();
        for (int i = 0; i < 500; i++)
            foreach (var kind in RunDirector.DefaultNodePlan(6, rng).Take(5)) // 5 interior slots per length-6 plan
                tally[kind] = tally.GetValueOrDefault(kind) + 1;

        Assert.DoesNotContain(RunNodeKind.BossBattle, tally.Keys); // the Boss is the apex, never an interior slot
        foreach (
            var kind in new[]
            {
                RunNodeKind.WildBattle,
                RunNodeKind.EliteBattle,
                RunNodeKind.Treasure,
                RunNodeKind.Shop,
                RunNodeKind.Mystery,
            }
        )
            Assert.True(
                tally.GetValueOrDefault(kind) > 0,
                $"{kind} never appeared in 2500 interior slots"
            );

        // Battle-heavy: wild battles are the plurality, and outnumber every feature bone.
        int wild = tally[RunNodeKind.WildBattle];
        Assert.All(
            tally.Where(kv => kv.Key != RunNodeKind.WildBattle),
            kv =>
                Assert.True(
                    wild > kv.Value,
                    $"WildBattle ({wild}) should outnumber {kv.Key} ({kv.Value})"
                )
        );
    }

    // --- Per-biome route length: randomised 4–6 (Boss-capped), seeded ---------------------------------------

    [Fact]
    public async Task BiomeMode_RollsBiomeRouteLength_BetweenFourAndSix_FullRangeReachable()
    {
        // The director rolls each biome's route length in [4, 6] (the default range) when the biome is entered,
        // then hands that length to the node-plan factory. A recording factory captures the rolled length; the
        // run faints on its first battle so exactly one biome (one roll) is observed per seed. Across seeds the
        // whole 4–6 range must be reachable, and every roll must stay in band.
        var observed = new HashSet<int>();
        for (int seed = 0; seed < 60; seed++)
        {
            int? rolled = null;
            // Capture the length the director asks for, then build a real Boss-capped plan of that length.
            Func<int, IRandomSource, IReadOnlyList<RunNodeKind>> recording = (n, r) =>
            {
                rolled ??= n;
                return RunDirector.DefaultNodePlan(n, r);
            };
            await RunUntilFirstFaint(seed, recording);

            Assert.NotNull(rolled);
            Assert.InRange(rolled!.Value, 4, 6); // default min/max — never out of band
            observed.Add(rolled.Value);
        }

        Assert.Equal(new[] { 4, 5, 6 }, observed.Order().ToArray()); // not stuck on one length
    }

    [Fact]
    public async Task BiomeMode_BiomeRouteLength_IsReproducibleFromSeed()
    {
        async Task<int?> RollFor(int seed)
        {
            int? rolled = null;
            Func<int, IRandomSource, IReadOnlyList<RunNodeKind>> recording = (n, r) =>
            {
                rolled ??= n;
                return RunDirector.DefaultNodePlan(n, r);
            };
            await RunUntilFirstFaint(seed, recording);
            return rolled;
        }

        Assert.Equal(await RollFor(4242), await RollFor(4242)); // same seed → same biome length
    }

    // The tier→"trainer-owned" wiring for XP: a Boss node is a trainer-analog tier, so its win must carry the
    // Gen-1 trainer ×1.5 bonus (proving BattleRunEvent maps the tier → Battle's trainerBattle → the seam).
    // RunRules is left at Default (1.0) to isolate the Gen-1 factor from the roguelite curve.
    [Fact]
    public async Task BossBattle_AwardsGen1TrainerXpBonus_NotThePlainWildAward()
    {
        const int enemyLevel = 40;
        const int enemyBaseExp = 100;
        int wildAward = (int)Math.Floor((double)enemyBaseExp * enemyLevel / 7); // 571
        int trainerAward = (int)Math.Floor(1.5 * enemyBaseExp * enemyLevel / 7); // 857

        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        var player = Fighter("Player", hp: 400, attack: 999, speed: 100, level: 50);

        // Every biome is a single Boss node. First Boss is a one-shot pushover (the win we measure); the second
        // biome's Boss is unbeatable and ends the run.
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
                    ? Fighter("Boss", hp: 1, attack: 5, speed: 1, level: enemyLevel)
                    : Fighter("FinalBoss", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = enemyBaseExp;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            nodePlanFactory: (_, _) => [RunNodeKind.BossBattle]
        );

        await runner.RunAsync();

        var firstGain = recorder.Of<ExperienceGained>().First();
        Assert.Equal(trainerAward, firstGain.Amount);
        Assert.NotEqual(wildAward, firstGain.Amount); // it's the trainer-boosted amount, not the wild one
    }

    // Runs a solo-biome run that faints on its first battle node, so exactly one biome's route is laid out (one
    // length roll captured by nodePlanFactory). The director uses its default 4–6 length range.
    private static async Task RunUntilFirstFaint(
        int seed,
        Func<int, IRandomSource, IReadOnlyList<RunNodeKind>> nodePlanFactory
    )
    {
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        var player = Fighter("Player", hp: 50, attack: 1, speed: 1, level: 50); // slow & weak: loses at once
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) =>
        {
            var enemy = Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: new RecordingEmitter(),
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(seed),
            playableBiomes: [solo],
            nodePlanFactory: nodePlanFactory // default min/max (4–6) — not overridden
        );

        await runner.RunAsync();
    }

    // --- Node dispatch: tiers reach the supplier, interaction bones emit + advance the biome ----------------

    [Fact]
    public async Task BiomeMode_DispatchesEveryNodeKind_WithBannersTiersAndBiomeAdvance()
    {
        // A dead-end solo biome: after its Poké Center the only route option is itself again, so the run loops
        // biomes until the player loses. The supplier feeds pushovers for the first biome (3 battle nodes) then
        // an unbeatable foe in the second biome's opener, ending the run after exactly one full biome.
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);

        // One biome's worth of route exercising all six kinds; the Boss is the apex (last).
        IReadOnlyList<RunNodeKind> plan =
        [
            RunNodeKind.WildBattle,
            RunNodeKind.EliteBattle,
            RunNodeKind.Shop,
            RunNodeKind.Treasure,
            RunNodeKind.Mystery,
            RunNodeKind.BossBattle,
        ];

        var player = Fighter("Player", hp: 300, attack: 999, speed: 100, level: 50);
        var tiersSeen = new List<EncounterTier>();
        int built = 0;
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            tier
        ) =>
        {
            built++;
            tiersSeen.Add(tier);
            // Battles 1–3 are the first biome's wild/elite/boss nodes (pushovers); battle 4 is the second
            // biome's opener and ends the run.
            var enemy =
                built <= 3
                    ? Fighter($"Push{built}", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"), // player: auto-picks the only biome (default ChooseBiomeAsync), scripts moves
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            minEventsPerBiome: 6,
            maxEventsPerBiome: 6,
            nodePlanFactory: (_, _) => plan
        );

        await runner.RunAsync();

        // The three battle nodes handed the supplier their tier, in plan order: wild → Normal, elite → Elite,
        // boss → Boss. The fourth call (second biome's opener) is a wild battle again.
        Assert.Equal(
            new[]
            {
                EncounterTier.Normal,
                EncounterTier.Elite,
                EncounterTier.Boss,
                EncounterTier.Normal,
            },
            tiersSeen.ToArray()
        );

        // Banners: Elite/Boss announce before their fight; the interaction bones each emit their kind. A plain
        // wild battle emits none.
        var banners = recorder.Of<RunNodeEntered>().Select(e => e.Kind).ToList();
        Assert.Equal(
            new[] { "EliteBattle", "Shop", "Treasure", "Mystery", "BossBattle" },
            banners.ToArray()
        );

        // The interaction nodes advanced the biome (no enemy built), so the biome reached its Poké Center cap
        // exactly once, after all six nodes resolved — proof the bones consume a slot.
        Assert.Single(recorder.Of<PlayerRecovered>());
        Assert.Equal(3, Assert.Single(recorder.Of<RunEnded>()).BattlesWon); // only the 3 first-biome wins count
    }

    [Fact]
    public async Task BiomeMode_RunDepth_CountsInteractionNodesNotJustWins()
    {
        // Biome-position depth (3c-2): the depth the supplier scales to counts every node traversed, including
        // the interaction bones — not just battle wins. With a Shop opening each biome, the depths the battles
        // see skip ahead by the shop they followed.
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        IReadOnlyList<RunNodeKind> plan =
        [
            RunNodeKind.Shop,
            RunNodeKind.WildBattle,
            RunNodeKind.BossBattle,
        ];

        var player = Fighter("Player", hp: 300, attack: 999, speed: 100, level: 50);
        var depthsSeen = new List<int>();
        int built = 0;
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            depth,
            _,
            _
        ) =>
        {
            built++;
            depthsSeen.Add(depth);
            // Biome 1's two battles (wild, boss) are pushovers; biome 2's opening battle ends the run.
            var enemy =
                built <= 2
                    ? Fighter($"Push{built}", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            minEventsPerBiome: 3,
            maxEventsPerBiome: 3,
            nodePlanFactory: (_, _) => plan
        );

        await runner.RunAsync();

        // Biome 1: shop (depth 0→1), wild battle sees 1, boss sees 2. Biome 2: shop (depth 3→4), wild sees 4.
        // The jump 2 → 4 (skipping 3) is the second biome's shop counting toward depth before its battle.
        Assert.Equal(new[] { 1, 2, 4 }, depthsSeen.ToArray());

        // BattlesWon stays independent of RunDepth: only the two won battles count it (biome-1 wild + boss),
        // never the shops — so an accidental BattlesWon++ in the interaction arm couldn't hide behind the depths.
        Assert.Equal(2, recorder.Of<RunEnded>().Single().BattlesWon);
    }

    [Fact]
    public async Task LegacyMode_NeverEmitsNodeBanners()
    {
        // No playable biomes → the legacy endless chain: plain wild battles, no node plan, no banners.
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            tier
        ) =>
        {
            Assert.Equal(EncounterTier.Normal, tier); // legacy battles are always the plain tier
            var enemy = Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0)
        );

        await runner.RunAsync();

        Assert.Empty(recorder.Of<RunNodeEntered>());
    }

    // --- Run Economy: reward-choice supplier wiring (every reward is a blocking pick-one-of-N) ---------------

    [Fact]
    public async Task BattleWin_WithRewardSupplier_OffersChoice_AppliesThePickedItemOption()
    {
        // Legacy chain (no biomes): one winnable battle, then an unbeatable foe ends the run so the reward
        // supplier is exercised exactly once. The choice offers an item (index 0) and a gold bag (index 1); the
        // scripted input picks the item, so the bag gains it and the wallet stays at 0.
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
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
                    ? Fighter("Push", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var wallet = new Wallet();
        var bag = new Bag();
        var input = new ScriptedInput("tackle").PicksReward(0); // take the item, not the gold
        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            input,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            healEveryNBattles: 0, // no Poké Center in the way of the single win being observed
            playerBag: bag,
            wallet: wallet,
            rewardSupplier: (ctx, _) =>
                ctx.Source == RunNodeKind.WildBattle
                    ? new RewardChoice([
                        new ItemRewardOption(17, "Potion", RewardRarity.Common),
                        new GoldRewardOption(25),
                    ])
                    : RewardChoice.None
        );

        await runner.RunAsync();

        // Picked the item → bag gains it, wallet untouched (the gold option was declined).
        Assert.Equal(0, wallet.Balance);
        Assert.Equal(1, bag.Count(17));

        // The choice was offered (Source "Battle", both options), and the chosen item announced via RewardGranted.
        var offered = Assert.Single(input.RewardChoicesOffered);
        Assert.Equal("Battle", offered.Source);
        Assert.Equal(2, offered.Options.Count);

        var granted = Assert.Single(recorder.Of<RewardGranted>());
        Assert.Equal("Battle", granted.Source);
        Assert.Equal(0, granted.Gold); // an item was taken, not gold
        Assert.Equal(["Potion"], granted.ItemNames);
    }

    [Fact]
    public async Task BattleWin_PickingTheGoldOption_CreditsWallet_NotTheBag()
    {
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
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
                    ? Fighter("Push", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var wallet = new Wallet();
        var bag = new Bag();
        var input = new ScriptedInput("tackle").PicksReward(1); // take the gold bag, not the item
        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            input,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            healEveryNBattles: 0,
            playerBag: bag,
            wallet: wallet,
            rewardSupplier: (ctx, _) =>
                ctx.Source == RunNodeKind.WildBattle
                    ? new RewardChoice([
                        new ItemRewardOption(17, "Potion", RewardRarity.Common),
                        new GoldRewardOption(25),
                    ])
                    : RewardChoice.None
        );

        await runner.RunAsync();

        Assert.Equal(25, wallet.Balance); // gold taken
        Assert.Equal(0, bag.Count(17)); // item declined

        var granted = Assert.Single(recorder.Of<RewardGranted>());
        Assert.Equal(25, granted.Gold);
        Assert.Equal(25, granted.GoldTotal);
        Assert.Empty(granted.ItemNames);
    }

    [Fact]
    public async Task BattleWin_NoRewardRolled_OffersNoChoice_EmitsNoRewardGranted()
    {
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) => Task.FromResult(Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50));

        var input = new ScriptedInput("tackle");
        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            input,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0)
        // no wallet / rewardSupplier — defaults to RewardChoice.None
        );

        await runner.RunAsync();

        Assert.Empty(recorder.Of<RewardChoiceOffered>());
        Assert.Empty(recorder.Of<RewardGranted>());
        Assert.Empty(input.RewardChoicesOffered);
    }

    [Fact]
    public async Task TreasureAndMystery_RollRewards_OfferChoicesAndApplyThePicks()
    {
        // A single biome, Treasure → Mystery → Boss. The Boss is unbeatable, so the run ends at it — but only
        // AFTER the two interaction nodes ahead of it have offered their rewards. This bounds the run to exactly
        // one biome's worth of rewards (a pushover boss in a dead-end biome would loop forever). The input picks
        // option 0 at each: the Treasure's item and the Mystery's (only) gold option.
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        IReadOnlyList<RunNodeKind> plan =
        [
            RunNodeKind.Treasure,
            RunNodeKind.Mystery,
            RunNodeKind.BossBattle,
        ];

        var player = Fighter("Player", hp: 300, attack: 999, speed: 100, level: 50);
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) => Task.FromResult(Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50));

        var wallet = new Wallet();
        var bag = new Bag();
        var input = new ScriptedInput("tackle").PicksReward(0);
        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            input,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            minEventsPerBiome: 3,
            maxEventsPerBiome: 3,
            nodePlanFactory: (_, _) => plan,
            playerBag: bag,
            wallet: wallet,
            rewardSupplier: (ctx, _) =>
                ctx.Source switch
                {
                    RunNodeKind.Treasure => new RewardChoice([
                        new ItemRewardOption(1, "Potion", RewardRarity.Common),
                        new GoldRewardOption(40),
                    ]),
                    RunNodeKind.Mystery => new RewardChoice([new GoldRewardOption(5)]),
                    _ => RewardChoice.None,
                }
        );

        await runner.RunAsync();

        Assert.Equal(5, wallet.Balance); // Treasure item taken (no gold), Mystery's only option is 5 gold
        Assert.Equal(1, bag.Count(1));

        var granted = recorder.Of<RewardGranted>().ToList();
        Assert.Equal(2, granted.Count);
        Assert.Equal("Treasure", granted[0].Source);
        Assert.Equal(0, granted[0].Gold);
        Assert.Equal(["Potion"], granted[0].ItemNames);
        Assert.Equal("Mystery", granted[1].Source);
        Assert.Equal(5, granted[1].Gold);
        Assert.Equal(5, granted[1].GoldTotal);
        Assert.Empty(granted[1].ItemNames);

        // Both interaction nodes offered a choice, in node order.
        Assert.Equal(2, input.RewardChoicesOffered.Count);
        Assert.Equal("Treasure", input.RewardChoicesOffered[0].Source);
        Assert.Equal("Mystery", input.RewardChoicesOffered[1].Source);
    }

    [Fact]
    public async Task RewardChoice_WithOutOfRangePick_FallsBackToTheFirstOption()
    {
        // A stale / malformed client index must never leave a reward unresolved: RewardResolution clamps an
        // out-of-range pick to option 0. PicksReward(99) overshoots every choice, so the Treasure's first option
        // (the Potion) is applied — never the gold, never nothing.
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        IReadOnlyList<RunNodeKind> plan = [RunNodeKind.Treasure, RunNodeKind.BossBattle];

        var player = Fighter("Player", hp: 300, attack: 999, speed: 100, level: 50);
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) => Task.FromResult(Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50));

        var wallet = new Wallet();
        var bag = new Bag();
        var input = new ScriptedInput("tackle").PicksReward(99); // overshoot — must clamp to option 0
        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            input,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            minEventsPerBiome: 2,
            maxEventsPerBiome: 2,
            nodePlanFactory: (_, _) => plan,
            playerBag: bag,
            wallet: wallet,
            rewardSupplier: (ctx, _) =>
                ctx.Source == RunNodeKind.Treasure
                    ? new RewardChoice([
                        new ItemRewardOption(1, "Potion", RewardRarity.Common),
                        new GoldRewardOption(40),
                    ])
                    : RewardChoice.None
        );

        await runner.RunAsync();

        // Clamped to option 0: the Potion is in the bag, and the gold bag (option 1) was NOT taken.
        Assert.Equal(1, bag.Count(1));
        Assert.Equal(0, wallet.Balance);

        var granted = Assert.Single(recorder.Of<RewardGranted>());
        Assert.Equal("Treasure", granted.Source);
        Assert.Equal(0, granted.Gold);
        Assert.Equal(["Potion"], granted.ItemNames);
    }

    [Fact]
    public async Task TreasureAndMystery_WithAutoSelectInput_DoNotBlockHeadlessRun()
    {
        // The default (non-interactive) input never overrides ChooseRewardAsync (auto-picks option 0), so a
        // headless run (AI/tests) sails through Treasure/Mystery without stalling on the modal. First biome's
        // Boss is a pushover (the win completes the biome and reaches its Poké Center); the second biome's Boss
        // is unbeatable, ending the run deterministically after exactly two Treasure/Mystery pairs.
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        IReadOnlyList<RunNodeKind> plan =
        [
            RunNodeKind.Treasure,
            RunNodeKind.Mystery,
            RunNodeKind.BossBattle,
        ];

        var player = Fighter("Player", hp: 300, attack: 999, speed: 100, level: 50);
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
                    ? Fighter("Push", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            minEventsPerBiome: 3,
            maxEventsPerBiome: 3,
            nodePlanFactory: (_, _) => plan,
            wallet: new Wallet(),
            rewardSupplier: (_, _) => new RewardChoice([new GoldRewardOption(10)])
        );

        await runner.RunAsync(); // completes at all — no deadlock on the modal — is the assertion

        int interactionRewards = recorder
            .Of<RewardGranted>()
            .Count(e => e.Source is "Treasure" or "Mystery");
        Assert.Equal(4, interactionRewards); // Treasure+Mystery × two biomes
        Assert.Single(recorder.Of<RunEnded>());
    }

    // --- Run Economy: the Shop node (spend-gold buy loop) --------------------------------------------------

    // A single biome, Shop → Boss. The Boss is unbeatable, so the run ends at it — but only after the shop
    // ahead of it has run its buy loop. Stock is a cheap Potion (8₽) and a premium Elixir (90₽).
    private static readonly ShopOffer TwoItemStock = new([
        new ShopOfferItem(17, "Potion", 8, RewardRarity.Common),
        new ShopOfferItem(20, "Elixir", 90, RewardRarity.Epic),
    ]);

    private static (RunDirector runner, RecordingEmitter recorder) BuildShopRun(
        ScriptedInput input,
        Wallet wallet,
        Bag bag,
        Func<ShopStockContext, IRandomSource, ShopOffer>? shopSupplier,
        int minShopBudget = 0
    )
    {
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        IReadOnlyList<RunNodeKind> plan = [RunNodeKind.Shop, RunNodeKind.BossBattle];
        var player = Fighter("Player", hp: 300, attack: 999, speed: 100, level: 50);
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) => Task.FromResult(Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50));

        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            input,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            minEventsPerBiome: 2,
            maxEventsPerBiome: 2,
            nodePlanFactory: (_, _) => plan,
            playerBag: bag,
            wallet: wallet,
            shopSupplier: shopSupplier,
            minShopBudget: minShopBudget
        );
        return (runner, recorder);
    }

    [Fact]
    public async Task Shop_OffersStock_BuySpendsWalletFillsBag_AndAnnouncesEachPurchase()
    {
        var wallet = new Wallet();
        wallet.Credit(100);
        var bag = new Bag();
        var input = new ScriptedInput("tackle").BuysThenLeaves(0, 0); // buy the Potion twice, then leave
        var (runner, recorder) = BuildShopRun(input, wallet, bag, (_, _) => TwoItemStock);

        await runner.RunAsync();

        Assert.Equal(84, wallet.Balance); // 100 − 8 − 8
        Assert.Equal(2, bag.Count(17)); // two Potions bought

        // The shop opened once with the entry balance, then a purchase event per buy carrying the running balance.
        var offered = Assert.Single(recorder.Of<ShopOffered>());
        Assert.Equal(100, offered.Balance);
        Assert.Equal(2, offered.Items.Count);

        var buys = recorder.Of<ShopItemPurchased>().ToList();
        Assert.Equal(2, buys.Count);
        Assert.Equal(("Potion", 8, 92), (buys[0].ItemName, buys[0].Price, buys[0].Balance));
        Assert.Equal(("Potion", 8, 84), (buys[1].ItemName, buys[1].Price, buys[1].Balance));

        // The input saw the balance fall across the visit (entry 100 → after buy 1 92 → after buy 2 84).
        Assert.Equal(
            new[] { 100, 92, 84 },
            input.ShopContextsSeen.Select(c => c.Balance).ToArray()
        );
    }

    [Fact]
    public async Task Shop_UnaffordableBuy_IsANoOp_NothingSpentOrBought()
    {
        var wallet = new Wallet();
        wallet.Credit(10); // can't afford either item's price... except the Potion (8) — so buy the Elixir (90)
        var bag = new Bag();
        var input = new ScriptedInput("tackle").BuysThenLeaves(1); // try the 90₽ Elixir with only 10₽
        var (runner, recorder) = BuildShopRun(input, wallet, bag, (_, _) => TwoItemStock);

        await runner.RunAsync();

        Assert.Equal(10, wallet.Balance); // untouched — the buy failed affordability
        Assert.Equal(0, bag.Count(20));
        Assert.Empty(recorder.Of<ShopItemPurchased>()); // no purchase announced
        Assert.Single(recorder.Of<ShopOffered>()); // but the shop did open
    }

    [Fact]
    public async Task Shop_OutOfRangeBuyIndex_IsANoOp_NothingSpentOrBought()
    {
        // A stale / malformed client index must never charge or strand the run: ShopRunEvent guards the index
        // and treats an out-of-range buy as a no-op, then re-prompts (the script leaves next). Distinct from the
        // unaffordable no-op above — this pins the index-bounds sibling guard.
        var wallet = new Wallet();
        wallet.Credit(100);
        var bag = new Bag();
        var input = new ScriptedInput("tackle").BuysThenLeaves(99); // overshoot every stock slot
        var (runner, recorder) = BuildShopRun(input, wallet, bag, (_, _) => TwoItemStock);

        await runner.RunAsync();

        Assert.Equal(100, wallet.Balance); // untouched
        Assert.Equal(0, bag.Entries.Values.Sum()); // nothing bought
        Assert.Empty(recorder.Of<ShopItemPurchased>());
        Assert.Single(recorder.Of<ShopOffered>()); // the shop still opened
    }

    [Fact]
    public async Task Shop_NoSupplier_ResolvesSilently_BannerOnly()
    {
        var wallet = new Wallet();
        wallet.Credit(100);
        var bag = new Bag();
        var input = new ScriptedInput("tackle").BuysThenLeaves(0); // would buy — but there's no stock to offer
        var (runner, recorder) = BuildShopRun(input, wallet, bag, shopSupplier: null);

        await runner.RunAsync();

        // The Shop node still announces itself, but with no stock it never offers or charges anything.
        Assert.Contains(recorder.Of<RunNodeEntered>(), e => e.Kind == "Shop");
        Assert.Empty(recorder.Of<ShopOffered>());
        Assert.Empty(recorder.Of<ShopItemPurchased>());
        Assert.Empty(input.ShopContextsSeen); // the buy loop was never entered
        Assert.Equal(100, wallet.Balance);
    }

    [Fact]
    public async Task Shop_GatedOut_WhenWalletBelowMinBudget_BecomesAWildBattle()
    {
        // Affordability gate: a Shop is a dead node if the player can't afford the cheapest item, so a biome
        // with the wallet below minShopBudget swaps its Shop slots for wild battles when the route is fixed. With
        // a 0₽ wallet and a budget of 8, the planned Shop→Boss becomes WildBattle→Boss — no shop offered, no shop
        // banner, and the swapped node runs as a battle (the unbeatable Bruiser ends the run there).
        var wallet = new Wallet(); // 0₽ — below budget
        var bag = new Bag();
        var input = new ScriptedInput("tackle").BuysThenLeaves(0);
        var (runner, recorder) = BuildShopRun(
            input,
            wallet,
            bag,
            (_, _) => TwoItemStock,
            minShopBudget: 8
        );

        await runner.RunAsync();

        Assert.Empty(recorder.Of<ShopOffered>());
        Assert.Empty(input.ShopContextsSeen); // the buy loop never ran
        Assert.DoesNotContain(recorder.Of<RunNodeEntered>(), e => e.Kind == "Shop"); // node 0 is a wild battle now
    }

    [Fact]
    public async Task Shop_Kept_WhenWalletMeetsMinBudget_AndTheCheapestItemIsAffordable()
    {
        // The gate's boundary: a wallet exactly at minShopBudget keeps the Shop, and the cheapest item is buyable.
        var wallet = new Wallet();
        wallet.Credit(8); // exactly the budget / the Potion's price
        var bag = new Bag();
        var input = new ScriptedInput("tackle").BuysThenLeaves(0); // buy the 8₽ Potion
        var (runner, recorder) = BuildShopRun(
            input,
            wallet,
            bag,
            (_, _) => TwoItemStock,
            minShopBudget: 8
        );

        await runner.RunAsync();

        Assert.Single(recorder.Of<ShopOffered>()); // shop kept — wallet cleared the budget
        Assert.Equal(0, wallet.Balance); // spent the 8₽
        Assert.Equal(1, bag.Count(17));
    }

    [Fact]
    public async Task Shop_BuyRefused_WhenBagSlotAtNinetyNine_NoChargeNoPurchase()
    {
        // The Gen 1 99-per-slot ceiling: a buy that would overfill a slot is refused *before* charging, so the
        // wallet is never spent on a clamped no-op. The bag already holds 99 Potions; the buy is a no-op.
        var wallet = new Wallet();
        wallet.Credit(100);
        var bag = new Bag();
        bag.Add(17, Bag.MaxPerSlot); // 99 Potions — the slot is full
        var input = new ScriptedInput("tackle").BuysThenLeaves(0); // try to buy another Potion
        var (runner, recorder) = BuildShopRun(input, wallet, bag, (_, _) => TwoItemStock);

        await runner.RunAsync();

        Assert.Equal(100, wallet.Balance); // untouched — the full-slot buy was refused before spending
        Assert.Equal(Bag.MaxPerSlot, bag.Count(17)); // still 99, not overfilled
        Assert.Empty(recorder.Of<ShopItemPurchased>());
        Assert.Single(recorder.Of<ShopOffered>());
    }

    [Fact]
    public async Task Shop_WithAutoSelectInput_DoesNotBlockHeadlessRun()
    {
        // The default (non-interactive) input never overrides ChooseShopActionAsync (leaves at once), so a
        // headless run sails past the shop without buying or deadlocking on the modal.
        var wallet = new Wallet();
        wallet.Credit(100);
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        IReadOnlyList<RunNodeKind> plan = [RunNodeKind.Shop, RunNodeKind.BossBattle];
        var player = Fighter("Player", hp: 300, attack: 999, speed: 100, level: 50);
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) => Task.FromResult(Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50));

        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            minEventsPerBiome: 2,
            maxEventsPerBiome: 2,
            nodePlanFactory: (_, _) => plan,
            wallet: wallet,
            shopSupplier: (_, _) => TwoItemStock
        );

        await runner.RunAsync(); // completing at all (no deadlock) is the assertion

        Assert.Single(recorder.Of<ShopOffered>());
        Assert.Empty(recorder.Of<ShopItemPurchased>()); // auto input bought nothing
        Assert.Single(recorder.Of<RunEnded>());
        Assert.Equal(100, wallet.Balance); // nothing spent headless
    }

    private static Creature Fighter(string name, int hp, int attack, int speed, int level)
    {
        var c = new Creature(name)
        {
            Level = level,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(level);
        c.Attributes.MaxHP = hp;
        c.Attributes.HP = hp;
        c.Attributes.Attack = attack;
        c.Attributes.Speed = speed;
        c.AddAttack(
            new Attack
            {
                Name = "tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
                PowerPointsMax = 99,
            }
        );
        return c;
    }
}
