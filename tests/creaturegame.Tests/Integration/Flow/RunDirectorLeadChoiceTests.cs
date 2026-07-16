using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// The between-biome lead choice in <see cref="RunDirector"/> (Phase 4 Stage 1d): at a biome boundary — after the
/// Poké Center, before the route choice — the player picks which party member leads into the next biome, when the
/// party holds more than one creature. Reassigns <see cref="Party.Lead"/> (⇒ <see cref="RunState.Player"/>). This
/// is a between-biome choice only; the battle engine is untouched (not in-battle switching). Interim faint model
/// through Stages 1–2 stands: the lead fainting still ends the run.
/// </summary>
public class RunDirectorLeadChoiceTests
{
    [Fact]
    public async Task LeadChoice_AtBoundary_ReassignsTheActiveCreature_ForTheNextBiome()
    {
        // A two-member party in a dead-end solo biome (one Boss node → its Poké Center caps it, then re-choose).
        // Biome 1's Boss is a pushover; at the boundary the lead choice picks member 1; biome 2's Boss is then
        // fought by the NEW lead and is unbeatable, ending the run. The supplier records which creature battled
        // each biome, proving the swap took effect on the actual battler.
        var lead0 = Fighter("Alpha", hp: 300, attack: 999, speed: 100, level: 50);
        var lead1 = Fighter("Bravo", hp: 300, attack: 999, speed: 100, level: 50);
        var party = new Party(lead0);
        party.Add(lead1);

        var battlers = new List<string>();
        int built = 0;
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            p,
            _,
            _,
            _
        ) =>
        {
            built++;
            battlers.Add(p.Name); // the lead handed to this battle
            var enemy =
                built == 1
                    ? Fighter("Boss1", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Boss2", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("tackle").PicksLead(1); // lead into biome 2 with Bravo
        var (runner, recorder) = BuildBoundaryRun(party, lead0, supplier, input);

        await runner.RunAsync();

        // The choice fired once and reassigned the lead: Bravo led biome 2. The battler record is the
        // forced-switch-immune proof (it captures the lead handed to each biome's battle at its start). Stage 3's
        // forced switch may bring Alpha back in when Bravo faints to the unbeatable biome-2 boss, so the *final*
        // Party.Lead is no longer the choice's observable — the battler record and the LeadChanged event are.
        Assert.Single(input.LeadChoicesOffered);
        var changed = Assert.Single(recorder.Of<LeadChanged>());
        Assert.Equal("Bravo", changed.Name);
        Assert.Equal(new[] { "Alpha", "Bravo" }, battlers.ToArray());

        // A PartyUpdated snapshot from the swap flagged the new lead (a later forced-switch snapshot may re-flag
        // Alpha, so match on any snapshot flagging Bravo rather than the last one).
        Assert.Contains(
            recorder.Of<PartyUpdated>(),
            s => s.Members.Single(m => m.IsLead).Name == "Bravo"
        );
    }

    [Fact]
    public async Task LeadChoice_FiresAfterThePokeCenter_AndBeforeTheNextRouteChoice()
    {
        var lead0 = Fighter("Alpha", hp: 300, attack: 999, speed: 100, level: 50);
        var party = new Party(lead0);
        party.Add(Fighter("Bravo", hp: 300, attack: 999, speed: 100, level: 50));

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
                    ? Fighter("Boss1", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Boss2", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("tackle").PicksLead(1);
        var (runner, recorder) = BuildBoundaryRun(party, lead0, supplier, input);

        await runner.RunAsync();

        var events = recorder.Events.ToList();
        int recoveredIdx = events.FindIndex(e => e is PlayerRecovered);
        int leadOfferIdx = events.FindIndex(e => e is LeadChoiceOffered);
        // The SECOND route choice (biome 1 → biome 2), not the run-start one.
        int secondChoiceIdx = events.FindIndex(
            events.FindIndex(e => e is BiomeChoiceOffered) + 1,
            e => e is BiomeChoiceOffered
        );

        Assert.True(recoveredIdx >= 0 && leadOfferIdx >= 0 && secondChoiceIdx >= 0);
        Assert.True(
            recoveredIdx < leadOfferIdx && leadOfferIdx < secondChoiceIdx,
            $"expected PlayerRecovered({recoveredIdx}) < LeadChoiceOffered({leadOfferIdx}) < 2nd BiomeChoiceOffered({secondChoiceIdx})"
        );
    }

    [Fact]
    public async Task LeadChoice_KeepingTheCurrentLead_IsANoOp()
    {
        // The prompt still fires (party > 1), but keeping the current lead (the ScriptedInput default returns the
        // current index) reassigns nothing: no LeadChanged, and Alpha still leads biome 2 (the battler record —
        // Stage 3's forced switch changes the *final* lead on the biome-2 wipe, so the battler is the proof).
        var lead0 = Fighter("Alpha", hp: 300, attack: 999, speed: 100, level: 50);
        var party = new Party(lead0);
        party.Add(Fighter("Bravo", hp: 300, attack: 999, speed: 100, level: 50));

        var battlers = new List<string>();
        int built = 0;
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            p,
            _,
            _,
            _
        ) =>
        {
            built++;
            battlers.Add(p.Name); // the lead handed to this biome's battle
            var enemy =
                built == 1
                    ? Fighter("Boss1", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Boss2", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("tackle"); // no PicksLead → keeps the current lead
        var (runner, recorder) = BuildBoundaryRun(party, lead0, supplier, input);

        await runner.RunAsync();

        Assert.Single(input.LeadChoicesOffered); // the choice was offered
        Assert.Empty(recorder.Of<LeadChanged>()); // …but nothing changed
        Assert.Equal(new[] { "Alpha", "Alpha" }, battlers.ToArray()); // Alpha still led biome 2 (kept the lead)
    }

    [Fact]
    public async Task LeadChoice_OutOfRangePick_IsANoOp()
    {
        // A stale / malformed client index (the hub forwards an arbitrary int) must never strand the run or swap
        // to a non-existent slot: an out-of-range pick keeps the current lead, emitting no LeadChanged.
        var lead0 = Fighter("Alpha", hp: 300, attack: 999, speed: 100, level: 50);
        var party = new Party(lead0);
        party.Add(Fighter("Bravo", hp: 300, attack: 999, speed: 100, level: 50));

        var battlers = new List<string>();
        int built = 0;
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            p,
            _,
            _,
            _
        ) =>
        {
            built++;
            battlers.Add(p.Name);
            var enemy =
                built == 1
                    ? Fighter("Boss1", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Boss2", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("tackle").PicksLead(99); // overshoot every slot
        var (runner, recorder) = BuildBoundaryRun(party, lead0, supplier, input);

        await runner.RunAsync();

        Assert.Single(input.LeadChoicesOffered); // the choice fired
        Assert.Empty(recorder.Of<LeadChanged>()); // …but the out-of-range pick changed nothing
        Assert.Equal(new[] { "Alpha", "Alpha" }, battlers.ToArray()); // Alpha still led biome 2
    }

    [Fact]
    public async Task LeadChoice_OnASwap_KeepsEachCreaturesOwnStatus_NoLeakOntoTheSwitchIn()
    {
        // The multi-creature carry model (STATE_MODEL.md §2): each creature carries its OWN out-of-battle status
        // (Creature.CarriedStatus). Exercised surgically on LeadChoiceEvent with a Poisoned outgoing lead (the
        // declined-Poké-Center case): after swapping to a healthy member, the outgoing lead KEEPS its Poison on
        // the bench, and the switch-in enters on its own (status-free) — the previous lead's status never leaks.
        var lead0 = Fighter("Alpha", hp: 300, attack: 999, speed: 100, level: 50);
        var lead1 = Fighter("Bravo", hp: 300, attack: 999, speed: 100, level: 50);
        lead0.CarriedStatus = new CarriedStatus(StatusCondition.Poison, 0);
        var party = new Party(lead0);
        party.Add(lead1);
        var state = new RunState(party);

        var input = new ScriptedInput().PicksLead(1);
        var recorder = new RecordingEmitter();
        var ctx = new RunContext(state, recorder, input, new SeededRandomSource(0));

        await new LeadChoiceEvent().RunAsync(ctx);

        Assert.Same(lead1, party.Lead); // the swap happened
        Assert.Equal(StatusCondition.Poison, lead0.CarriedStatus?.Status); // benched member keeps its ailment
        Assert.Null(lead1.CarriedStatus); // …and the switch-in enters on its own footing (no leak)
        Assert.Single(recorder.Of<LeadChanged>());
    }

    [Fact]
    public async Task LeadChoice_AfterADeclinedPokeCenter_KeepsTheStillStatusedOutgoingLeadsStatus_ThroughRunDirector()
    {
        // End-to-end through the real RunDirector pipeline (not the surgical LeadChoiceEvent call above): a lead
        // that reaches the biome boundary STILL ailed — because the player DECLINED the Poké Center — must keep
        // its status when it benches on a swap, and the switch-in must enter status-free. Alpha enters biome 1
        // already Poisoned, wins (re-capturing the Poison out of battle), skips the Center, then the boundary lead
        // choice swaps to Bravo; biome 2's unbeatable Boss ends the run.
        var lead0 = Fighter("Alpha", hp: 300, attack: 999, speed: 100, level: 50);
        var lead1 = Fighter("Bravo", hp: 300, attack: 999, speed: 100, level: 50);
        lead0.CarriedStatus = new CarriedStatus(StatusCondition.Poison, 0); // enters biome 1 already poisoned
        var party = new Party(lead0);
        party.Add(lead1);

        var battlers = new List<string>();
        int built = 0;
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier = (
            p,
            _,
            _,
            _
        ) =>
        {
            built++;
            battlers.Add(p.Name);
            var enemy =
                built == 1
                    ? Fighter("Boss1", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Boss2", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("tackle").DeclinesRecovery().PicksLead(1);
        var (runner, _) = BuildBoundaryRun(party, lead0, supplier, input);

        await runner.RunAsync();

        Assert.Equal(new[] { "Alpha", "Bravo" }, battlers.ToArray()); // the swap took effect: Bravo led biome 2
        // The outgoing lead kept its Poison on the bench (the Center was declined, so nothing cured it), and the
        // switch-in entered on its own footing — the previous lead's status never leaked onto it. (Stage 3's
        // forced switch may bring Alpha back in on the biome-2 wipe; the loss path never re-captures status, so
        // Alpha's carried Poison and Bravo's clean slate both stand.)
        Assert.Equal(StatusCondition.Poison, lead0.CarriedStatus?.Status);
        Assert.Null(lead1.CarriedStatus);
    }

    [Fact]
    public async Task LeadChoice_NeverFires_ForALoneStarterParty()
    {
        // A single-creature run never sees the prompt (there's no one to switch to) — the gate is Party.Count > 1.
        var lead0 = Fighter("Solo", hp: 300, attack: 999, speed: 100, level: 50);
        var party = new Party(lead0); // one member

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
                    ? Fighter("Boss1", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Boss2", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("tackle").PicksLead(0);
        var (runner, recorder) = BuildBoundaryRun(party, lead0, supplier, input);

        await runner.RunAsync();

        Assert.Empty(input.LeadChoicesOffered);
        Assert.Empty(recorder.Of<LeadChoiceOffered>());
        Assert.Empty(recorder.Of<LeadChanged>());
    }

    // A biome-mode run over a dead-end solo biome whose route is a single Boss node — so biome 1's Boss win caps
    // the biome (Poké Center), the lead choice fires at the boundary, then biome 2's Boss ends the run.
    private static (RunDirector runner, RecordingEmitter recorder) BuildBoundaryRun(
        Party party,
        Creature lead,
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier,
        ScriptedInput input
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
            new RunDirectorOptions
            {
                Emitter = recorder,
                Rules = new ScriptableRules().Deterministic(),
                Rng = new SeededRandomSource(0),
                PlayableBiomes = [solo],
                MinEventsPerBiome = 1,
                MaxEventsPerBiome = 1,
                NodePlanFactory = (_, _) => [RunNodeKind.BossBattle],
                Party = party,
            }
        );
        return (runner, recorder);
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
