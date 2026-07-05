using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// Phase 3c-1 node-kind bones in <see cref="RunDirector"/>: a biome's route is a plan of <see cref="RunNodeKind"/>
/// nodes the director dispatches — three battle tiers (mapped to a generation-agnostic <see cref="EncounterTier"/>
/// the supplier consumes) and three interaction bones (Shop/Treasure/Mystery: emit a banner, advance the biome,
/// no behaviour yet). The default layout caps each biome with a Boss apex. Enemies are supplied by a delegate
/// (no DB), so these pin sequencing + the tier/banner wiring, not encounter contents.
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

    // --- Run Economy: reward supplier wiring (battle drops inline; Treasure/Mystery block on ack) ------------

    [Fact]
    public async Task BattleWin_WithRewardSupplier_CreditsWalletAndBag_EmitsRewardGranted_NonBlocking()
    {
        // Legacy chain (no biomes): one winnable battle, then an unbeatable foe ends the run so the reward
        // supplier is exercised exactly once.
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
            rng: new SeededRandomSource(0),
            healEveryNBattles: 0, // no Poké Center in the way of the single win being observed
            playerBag: bag,
            wallet: wallet,
            rewardSupplier: (ctx, _) =>
                ctx.Source == RunNodeKind.WildBattle
                    ? new RunReward(25, [new RewardedItem(17, "Potion")])
                    : RunReward.Empty
        );

        await runner.RunAsync();

        Assert.Equal(25, wallet.Balance);
        Assert.Equal(1, bag.Count(17));

        var granted = Assert.Single(recorder.Of<RewardGranted>());
        Assert.Equal("Battle", granted.Source);
        Assert.Equal(25, granted.Gold);
        Assert.Equal(25, granted.GoldTotal);
        Assert.Equal(["Potion"], granted.ItemNames);

        // A battle-win reward is inline, not a blocking prompt — the player input never sees an ack.
        Assert.Empty(input.RewardAcksReceived);
    }

    [Fact]
    public async Task BattleWin_NoRewardRolled_EmitsNoRewardGranted()
    {
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
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
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0)
        // no wallet / rewardSupplier — defaults to RunReward.Empty
        );

        await runner.RunAsync();

        Assert.Empty(recorder.Of<RewardGranted>());
    }

    [Fact]
    public async Task TreasureAndMystery_RollRewards_ApplyThemAndBlockOnAck()
    {
        // A single biome, Treasure → Mystery → Boss. The Boss is unbeatable, so the run ends at it — but only
        // AFTER the two interaction nodes ahead of it have emitted their rewards. This bounds the run to exactly
        // one biome's worth of rewards (a pushover boss in a dead-end biome would loop forever).
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
                    RunNodeKind.Treasure => new RunReward(40, [new RewardedItem(1, "Master Ball")]),
                    RunNodeKind.Mystery => new RunReward(5, []),
                    _ => RunReward.Empty,
                }
        );

        await runner.RunAsync();

        Assert.Equal(45, wallet.Balance); // 40 (Treasure) + 5 (Mystery)
        Assert.Equal(1, bag.Count(1));

        var granted = recorder.Of<RewardGranted>().ToList();
        Assert.Equal(2, granted.Count);
        Assert.Equal("Treasure", granted[0].Source);
        Assert.Equal(40, granted[0].Gold);
        Assert.Equal(40, granted[0].GoldTotal); // wallet total after this credit
        Assert.Equal(["Master Ball"], granted[0].ItemNames);
        Assert.Equal("Mystery", granted[1].Source);
        Assert.Equal(5, granted[1].Gold);
        Assert.Equal(45, granted[1].GoldTotal);
        Assert.Empty(granted[1].ItemNames);

        // Both interaction nodes blocked on the player's acknowledgement, in node order.
        Assert.Equal(2, input.RewardAcksReceived.Count);
        Assert.Equal("Treasure", input.RewardAcksReceived[0].Source);
        Assert.Equal("Mystery", input.RewardAcksReceived[1].Source);
    }

    [Fact]
    public async Task TreasureAndMystery_WithAutoSelectInput_DoNotBlockHeadlessRun()
    {
        // The default (non-interactive) input never overrides AcknowledgeRewardAsync, so a headless run
        // (AI/tests) sails through Treasure/Mystery without stalling on the ack prompt. First biome's Boss is
        // a pushover (the win completes the biome and reaches its Poké Center); the second biome's Boss is
        // unbeatable, ending the run deterministically after exactly two Treasure/Mystery pairs.
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
            rewardSupplier: (_, _) => new RunReward(10, [])
        );

        await runner.RunAsync(); // completes at all — no deadlock on the ack prompt — is the assertion

        int interactionRewards = recorder
            .Of<RewardGranted>()
            .Count(e => e.Source is "Treasure" or "Mystery");
        Assert.Equal(4, interactionRewards); // Treasure+Mystery × two biomes
        Assert.Single(recorder.Of<RunEnded>());
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
