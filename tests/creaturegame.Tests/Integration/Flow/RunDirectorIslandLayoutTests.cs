using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;
using creaturegame.Tests.Unit;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// Stage 4c steps 2–3 (`docs/TODO.md`) — the Town Map's <see cref="IslandLayout"/> gets wired into
/// <see cref="RunDirector"/>: computed exactly once, at run start (map-selection time), from the same shared
/// <see cref="IRandomSource"/> every other per-run roll draws from — never a fresh RNG, and never recomputed
/// per biome — then projected onto the <see cref="RegionMapRevealed"/> wire payload (step 3; the value-level
/// wire-shape pin itself lives in <c>WebEventContractTests</c>).
/// </summary>
public class RunDirectorIslandLayoutTests
{
    // A tiny connected graph, the same shape RunDirectorBiomeTests uses: A ── B, A ── C.
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

    [Fact]
    public async Task BiomeMode_ComputesTheIslandLayout_FromTheSharedRng_MatchingADirectGeneratorCall()
    {
        // Same seed, same playable subgraph, and no other draw precedes it either way (the director's very
        // first RNG draw in biome mode is this one) — so the cached layout must equal a bare Generate call.
        var expected = IslandLayoutGenerator.Generate(Playable, new SeededRandomSource(0));

        var runner = BuildRunner(Playable, new SeededRandomSource(0));
        await runner.RunAsync();

        var actual = runner.IslandLayout;
        Assert.NotNull(actual);
        Assert.Equal(expected.Width, actual!.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Positions, actual.Positions);
        Assert.Equal(
            expected.Routes.Select(r =>
                (r.FromBiomeId, r.ToBiomeId, Path: string.Join(",", r.Cells))
            ),
            actual.Routes.Select(r => (r.FromBiomeId, r.ToBiomeId, Path: string.Join(",", r.Cells)))
        );
    }

    [Fact]
    public async Task BiomeMode_CachesTheLayout_CoveringTheWholePlayableSet_NotJustTheVisitedBiome()
    {
        // The unbeatable enemy ends the run on the very first node, so only one biome is ever actually entered —
        // but the cached layout still places every playable biome, proof it was laid out once up front from the
        // whole subgraph (map-selection time), not rebuilt/narrowed per biome as the run progresses.
        var runner = BuildRunner(Playable, new SeededRandomSource(0));
        await runner.RunAsync();

        Assert.NotNull(runner.IslandLayout);
        Assert.Equal(
            Playable.Select(b => b.Id).OrderBy(id => id, StringComparer.Ordinal),
            runner.IslandLayout!.Positions.Keys.OrderBy(id => id, StringComparer.Ordinal)
        );
    }

    [Fact]
    public async Task BiomeMode_RegionMapRevealed_LaysOutRealGridGeometry_ForAnyProfilesBiomeRoster()
    {
        // Stage 5 falsification leg (GENERATION_PROFILE.md §3): the Town Map must be driven by whatever
        // BiomeRoster a profile supplies, not hardcoded against Kanto's shape. TestAltProfile's two-biome fake
        // region is deliberately not Kanto data, so laying it out cleanly on the real wire event — grid bounds,
        // both biomes placed in-bounds, a real route between them — is the only way to observe the pipeline
        // (RunDirector → IslandLayoutGenerator → BuildRegionMap → RegionMapRevealed) is generation-agnostic.
        var altBiomes = TestAltProfile.Instance.BiomeRoster;
        var recorder = new RecordingEmitter();
        var runner = BuildRunner(altBiomes, new SeededRandomSource(0), recorder);

        await runner.RunAsync();

        var map = Assert.Single(recorder.Of<RegionMapRevealed>());
        Assert.True(map.Width > 0 && map.Height > 0);
        Assert.Equal(
            altBiomes.Select(b => b.Id).OrderBy(id => id, StringComparer.Ordinal),
            map.Biomes.Select(b => b.Id).OrderBy(id => id, StringComparer.Ordinal)
        );
        Assert.All(
            map.Biomes,
            b => Assert.True(b.X >= 0 && b.X < map.Width && b.Y >= 0 && b.Y < map.Height)
        );
        var route = Assert.Single(map.Routes); // the two alt biomes are neighbours → exactly one edge/route
        Assert.Equal(
            altBiomes.Select(b => b.Id).ToHashSet(),
            new HashSet<string> { route.FromBiomeId, route.ToBiomeId }
        );
    }

    [Fact]
    public async Task LegacyChain_NeverComputesAnIslandLayout()
    {
        // No PlayableBiomes supplied → biome mode never activates, so there's no Town Map to lay out.
        var player = Fighter("Player", hp: 1, attack: 1, speed: 1, level: 5);
        var enemy = Fighter("Enemy", hp: 999, attack: 999, speed: 999, level: 50);
        var runner = new RunDirector(
            player,
            (_, _, _, _) => Task.FromResult(enemy),
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            new RunDirectorOptions
            {
                Rules = new ScriptableRules().Deterministic(),
                Rng = new SeededRandomSource(0),
            }
        );

        await runner.RunAsync();

        Assert.Null(runner.IslandLayout);
    }

    private static RunDirector BuildRunner(
        IReadOnlyList<BiomeDefinition> playable,
        IRandomSource rng,
        RecordingEmitter? emitter = null
    )
    {
        var player = Fighter("Player", hp: 10, attack: 1, speed: 1, level: 50);
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) =>
        {
            var bruiser = Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            bruiser.SpeciesBaseExperience = 50;
            return Task.FromResult(bruiser); // unbeatable → the run ends on the very first node
        };

        return new RunDirector(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            new RunDirectorOptions
            {
                Emitter = emitter ?? new RecordingEmitter(),
                Rules = new ScriptableRules().Deterministic(),
                Rng = rng,
                PlayableBiomes = playable,
                MinEventsPerBiome = 1,
                MaxEventsPerBiome = 1,
                NodePlanFactory = (_, _) => [RunNodeKind.WildBattle],
            }
        );
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
