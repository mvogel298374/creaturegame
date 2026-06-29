using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// Phase 3b biome traversal in <see cref="RunDirector"/>: when a playable biome set is supplied the run becomes
/// a route through the biome graph — it opens with a route choice, runs that biome's themed encounters, caps
/// each biome with a Poké Center, then offers the next leg (the current biome's playable neighbours). Enemies
/// are supplied by a delegate (no DB), so these pin the sequencing + the biome threaded to the supplier, not
/// the encounter contents. With no biomes supplied the director falls back to the legacy endless chain.
/// </summary>
public class RunDirectorBiomeTests
{
    // A tiny authored graph:  A(Normal) ── B(Fire)   and   A ── C(Water).   B's and C's only neighbour is A.
    private static readonly BiomeDefinition A = new(
        "a",
        "Alpha",
        Region.Kanto,
        [DamageType.Normal],
        ["b", "c"]
    );
    private static readonly BiomeDefinition B = new(
        "b",
        "Bravo",
        Region.Kanto,
        [DamageType.Fire],
        ["a"]
    );
    private static readonly BiomeDefinition C = new(
        "c",
        "Charlie",
        Region.Kanto,
        [DamageType.Water],
        ["a"]
    );
    private static readonly IReadOnlyList<BiomeDefinition> Playable = [A, B, C];

    // These tests pin biome *traversal* (route open / theme / cap / neighbours), not the 3c-1 node-kind mix, so
    // they force an all-wild-battle plan — the biome is N battles, the way 3b modelled it. The node-plan variety
    // (Boss apex, interaction nodes) is exercised by RunDirectorNodeTests instead.
    private static readonly Func<int, IRandomSource, IReadOnlyList<RunNodeKind>> AllWildPlan = (
        n,
        _
    ) => Enumerable.Repeat(RunNodeKind.WildBattle, n).ToList();

    [Fact]
    public async Task BiomeMode_OpensWithRouteChoice_ThemesEncounters_AndCapsEachBiomeWithRecovery()
    {
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);

        int built = 0;
        var biomesSeen = new List<string?>();
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            biome,
            _
        ) =>
        {
            built++;
            biomesSeen.Add(biome?.Id);
            // Three pushovers clear the first biome; the fourth foe (in the second biome) ends the run.
            var enemy =
                built <= 3
                    ? Fighter($"Push{built}", hp: 1, attack: 5, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        // Pick B at the start, then A after B is cleared.
        var input = new BiomeScriptedInput(["b", "a"], "tackle");
        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: Playable,
            eventsPerBiome: 3,
            nodePlanFactory: AllWildPlan
        );

        await runner.RunAsync();

        // The run opened with a route choice — before any battle.
        var events = recorder.Events.ToList();
        Assert.True(
            events.FindIndex(e => e is BiomeChoiceOffered)
                < events.FindIndex(e => e is BattleStarted),
            "the route choice must precede the first encounter"
        );

        // Two route choices (run start + after the first biome's Poké Center); entered B then A.
        Assert.Equal(2, recorder.Of<BiomeChoiceOffered>().Count());
        Assert.Equal(
            new[] { "b", "a" },
            recorder.Of<BiomeEntered>().Select(e => e.BiomeId).ToArray()
        );

        // B themed its three encounters; the run-ending fourth foe was themed to A.
        Assert.Equal(new string?[] { "b", "b", "b", "a" }, biomesSeen.ToArray());

        // The first choice offers the whole playable set; the second offers only B's playable neighbours (A).
        Assert.Equal(
            new[] { "a", "b", "c" },
            input.OfferedOptions[0].Select(b => b.Id).Order().ToArray()
        );
        Assert.Equal(new[] { "a" }, input.OfferedOptions[1].Select(b => b.Id).ToArray());

        // Each cleared biome caps with a Poké Center — one here, for biome B.
        Assert.Single(recorder.Of<PlayerRecovered>());

        var runEnded = Assert.Single(recorder.Of<RunEnded>());
        Assert.Equal(3, runEnded.BattlesWon);
    }

    [Fact]
    public async Task BiomeMode_DeadEndWithNoPlayableNeighbours_FallsBackToAllPlayable()
    {
        // X's only neighbour "y" is not in the playable set; after X is cleared the next choice has no themed
        // neighbour to offer, so it must fall back to the whole playable set rather than stall.
        var x = new BiomeDefinition("x", "Xeno", Region.Kanto, [DamageType.Normal], ["y"]);
        var z = new BiomeDefinition("z", "Zeta", Region.Kanto, [DamageType.Water], ["x"]);
        IReadOnlyList<BiomeDefinition> playable = [x, z];

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
                    ? Fighter("Push", hp: 1, attack: 5, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new BiomeScriptedInput(["x"], "tackle"); // start on X, then take whatever is offered
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: new RecordingEmitter(),
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: playable,
            eventsPerBiome: 1, // a one-battle biome, so the second choice is reached quickly
            nodePlanFactory: AllWildPlan
        );

        await runner.RunAsync();

        Assert.Equal(2, input.OfferedOptions.Count);
        // The dead-end choice falls back to every playable biome.
        Assert.Equal(
            new[] { "x", "z" },
            input.OfferedOptions[1].Select(b => b.Id).Order().ToArray()
        );
    }

    [Fact]
    public async Task BiomeMode_OfferedRoute_IsReproducibleFromSeed()
    {
        var first = await CaptureOpeningRoute(seed: 12345);
        var same = await CaptureOpeningRoute(seed: 12345);

        Assert.Equal(first, same); // same seed → same offered biomes, same order
        Assert.Equal(3, first.Count); // sampled down to the option count, from the 18-biome Kanto set
    }

    // The opening route choice must always hand the starter at least one favourable lane — a biome whose theme
    // the starter's type hits super-effectively — so a run never opens with only bad matchups. Checked across
    // many seeds (the guarantee is unconditional, not luck) for two starters with clear Gen 1 coverage.
    [Theory]
    [InlineData(DamageType.Water)] // super-effective vs Fire / Ground / Rock
    [InlineData(DamageType.Electric)] // super-effective vs Water / Flying
    public async Task OpeningRoute_AlwaysOffersAtLeastOneFavourableMatchup(DamageType starterType)
    {
        var chart = Gen1TypeChart.Instance;
        for (int seed = 0; seed < 40; seed++)
        {
            var offered = await CaptureOpeningOffer(starterType, seed);
            Assert.Equal(3, offered.Count); // still a full 3-of-18 offer
            Assert.Contains(
                offered,
                b => b.Types.Any(t => chart.GetMultiplier(starterType, t) > 1.0)
            );
        }
    }

    // A starter with no super-effective coverage (pure Normal hits nothing for extra) can't be given a
    // favourable lane — the guarantee simply can't apply, so the opening offer is the plain seeded sample: no
    // crash, still a full option count.
    [Fact]
    public async Task OpeningRoute_StarterWithNoSuperEffectiveCoverage_StillOffersAFullRoute()
    {
        var offered = await CaptureOpeningOffer(DamageType.Normal, seed: 7);
        Assert.Equal(3, offered.Count);
    }

    // Runs until the player faints on the first encounter, so only the opening route choice is recorded; returns
    // the offered biome ids in order. Uses the real Kanto roster so the sample is a meaningful 3-of-18 draw.
    private static async Task<List<string>> CaptureOpeningRoute(int seed)
    {
        var player = Fighter("Player", hp: 200, attack: 1, speed: 1, level: 50); // slow & weak: loses at once
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
        var input = new BiomeScriptedInput([], "tackle"); // no script → takes the first offered each time
        var runner = new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: new RecordingEmitter(),
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(seed),
            playableBiomes: Biomes.Kanto,
            eventsPerBiome: 3,
            nodePlanFactory: AllWildPlan
        );

        await runner.RunAsync();
        return input.OfferedOptions[0].Select(b => b.Id).ToList();
    }

    // As CaptureOpeningRoute, but the starter's type is set so the favourable-matchup bias is exercised, and it
    // returns the offered biome definitions (so the test can inspect their themes). The player still faints on
    // the first encounter, so only the opening offer is captured.
    private static async Task<IReadOnlyList<BiomeDefinition>> CaptureOpeningOffer(
        DamageType starterType,
        int seed
    )
    {
        var player = new Creature("Player")
        {
            Level = 50,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = starterType,
        };
        player.CalculateStats();
        player.Experience = player.CalculateExperienceForLevel(50);
        player.Attributes.MaxHP = 200;
        player.Attributes.HP = 200;
        player.Attributes.Attack = 1;
        player.Attributes.Speed = 1; // slow & weak: loses at once, so only the opening route is recorded
        player.AddAttack(
            new Attack
            {
                Name = "tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
                PowerPointsMax = 99,
            }
        );

        var input = new BiomeScriptedInput([], "tackle");
        var runner = new RunDirector(
            player,
            (_, _, _, _) =>
                Task.FromResult(Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50)),
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: new RecordingEmitter(),
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(seed),
            playableBiomes: Biomes.Kanto,
            eventsPerBiome: 3,
            nodePlanFactory: AllWildPlan
        );

        await runner.RunAsync();
        return input.OfferedOptions[0];
    }

    [Fact]
    public async Task LegacyMode_NoBiomesSupplied_EmitsNoBiomeEvents_AndSuppliesNullBiome()
    {
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            biome,
            _
        ) =>
        {
            Assert.Null(biome); // legacy chain never themes an encounter
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
        ); // no playableBiomes → legacy mode

        await runner.RunAsync();

        Assert.Empty(recorder.Of<BiomeChoiceOffered>());
        Assert.Empty(recorder.Of<BiomeEntered>());
        Assert.Single(recorder.Of<RunEnded>());
    }

    // A player input that scripts both its moves (delegated to ScriptedInput) and its biome picks, and records
    // the options it was offered each route choice. An empty/exhausted biome script takes the first option.
    private sealed class BiomeScriptedInput(string[] biomeIds, params string[] moves) : IBattleInput
    {
        private readonly ScriptedInput _moves = new(moves);
        private readonly Queue<string> _biomes = new(biomeIds);

        public List<IReadOnlyList<BiomeDefinition>> OfferedOptions { get; } = [];

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            _moves.ChooseMoveAsync(context);

        public Task<string> ChooseBiomeAsync(BiomeChoiceContext context)
        {
            OfferedOptions.Add(context.Options);
            string id = _biomes.Count > 0 ? _biomes.Dequeue() : context.Options[0].Id;
            // Honour the script only if that biome is actually on offer; otherwise take the first (the same
            // fallback the director itself applies to an unknown id).
            return Task.FromResult(
                context.Options.Any(b => b.Id == id) ? id : context.Options[0].Id
            );
        }
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
