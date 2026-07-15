using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// Forced switch-on-faint through the real <see cref="RunDirector"/> pipeline (Encounter Logic Phase 4 Stage 3):
/// when the lead faints mid-encounter and a bench member is alive, the run does <em>not</em> end — the survivor
/// finishes the fight and becomes the active creature (<see cref="RunState.Player"/> = <see cref="Party.Lead"/>).
/// The run ends only when the <em>whole party</em> is down. Complements <see cref="BattleForcedSwitchTests"/>
/// (the engine-level behaviour) by proving the run-loop consequences: the win still counts, and the finisher —
/// not the fainted creature that started the fight — is what the director carries forward.
/// </summary>
public class RunDirectorForcedSwitchTests
{
    [Fact]
    public async Task Run_ContinuesPastALeadFaint_AndRunStatePlayerTracksTheSwitchedInFinisher()
    {
        // Biome plan: a winnable-via-switch Wild node, then an unbeatable Boss. The frail lead faints in the wild
        // encounter, the strong bench is sent in and wins (so the run continues and the win counts), then the Boss
        // downs the whole party. The finisher (bench) is the active creature the director carried forward.
        var lead = Fighter("Lead", hp: 10, attack: 1, speed: 1);
        var bench = Fighter("Bench", hp: 300, attack: 999, speed: 150);
        var party = new Party(lead);
        party.Add(bench);

        int built = 0;
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) =>
        {
            built++;
            // Wild foe: KOs the frail lead but falls to the bench. Boss: unbeatable — ends the run.
            var enemy =
                built == 1
                    ? Fighter("Wild", hp: 50, attack: 999, speed: 100)
                    : Fighter("Boss", hp: 999, attack: 999, speed: 999);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("tackle").PicksSwitchIn(1);
        var (runner, recorder) = BuildRun(
            party,
            lead,
            supplier,
            input,
            [RunNodeKind.WildBattle, RunNodeKind.BossBattle]
        );

        await runner.RunAsync();

        // The forced switch fired, the wild win still counted (the run continued past the lead's faint), and the
        // finisher (bench) is the active creature — RunState.Player tracks the switched-in survivor, not the
        // fainted lead that started the fight.
        Assert.NotEmpty(recorder.Of<CreatureSwitchedIn>());
        Assert.Equal(1, runner.State.BattlesWon);
        Assert.Same(bench, runner.State.Party.Lead);
        // The run ended (the Boss downed the whole party), once.
        Assert.Single(recorder.Of<RunEnded>());
    }

    [Fact]
    public async Task Run_Ends_WhenTheWholePartyFaints()
    {
        // Both members are frail and the sole Wild foe is unbeatable: the lead faints, the bench is sent in and
        // also faints — with no one left, the run ends (a loss, no win recorded), after the switch was offered.
        var lead = Fighter("Lead", hp: 10, attack: 1, speed: 1);
        var bench = Fighter("Bench", hp: 10, attack: 1, speed: 1);
        var party = new Party(lead);
        party.Add(bench);

        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            _,
            _,
            _,
            _
        ) =>
        {
            var enemy = Fighter("Foe", hp: 999, attack: 999, speed: 999);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("tackle").PicksSwitchIn(1);
        var (runner, recorder) = BuildRun(party, lead, supplier, input, [RunNodeKind.WildBattle]);

        await runner.RunAsync();

        Assert.NotEmpty(recorder.Of<CreatureSwitchedIn>()); // the bench was sent in…
        Assert.Equal(0, runner.State.BattlesWon); // …but the party still wiped — no win
        Assert.Single(recorder.Of<RunEnded>()); // and the run ended
    }

    // A biome-mode run over a dead-end solo biome whose route is the given node plan, so the tests control exactly
    // which encounters fire. Mirrors RunDirectorLeadChoiceTests.BuildBoundaryRun but with an injectable plan.
    private static (RunDirector runner, RecordingEmitter recorder) BuildRun(
        Party party,
        Creature lead,
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier,
        ScriptedInput input,
        IReadOnlyList<RunNodeKind> plan
    )
    {
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            lead,
            supplier,
            Gen1TypeChart.Instance,
            input,
            input,
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            playableBiomes: [solo],
            minEventsPerBiome: plan.Count,
            maxEventsPerBiome: plan.Count,
            nodePlanFactory: (_, _) => plan,
            party: party
        );
        return (runner, recorder);
    }

    private static Creature Fighter(string name, int hp, int attack, int speed)
    {
        var c = new Creature(name)
        {
            Level = 50,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(50);
        c.Attributes.MaxHP = hp;
        c.Attributes.HP = hp;
        c.Attributes.Attack = attack;
        c.Attributes.Defense = 100;
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
