using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
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
    // --- The default biome route layout (the placeholder generator; tuned curve is 3c-2) -------------------

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
            eventsPerBiome: 6,
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
