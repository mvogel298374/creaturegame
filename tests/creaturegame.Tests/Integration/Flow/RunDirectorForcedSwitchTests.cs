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

    [Fact]
    public async Task MutualKo_WithALiveBenchMember_CountsTheWin_AndPromptsForTheNextLeadInsteadOfEndingTheRun()
    {
        // The mutual-KO edge (ruling 2026-07-28). Lead and foe both poison each other on turn 1 and BOTH drop to
        // the same end-of-turn tick. Battle scores it a win (the enemy-faint check runs first) but offers no
        // in-battle forced switch — there is no enemy left to send anyone in against — so the run loop is what
        // must not end the run: a healthy bench member is still standing, and the whole-party rule says the run
        // continues. The player PICKS the next lead through the forced-switch prompt, and the result is a lead
        // reassignment (LeadChanged), not a send-in (no CreatureSwitchedIn — nobody takes the field).
        // Biome plan: the mutual-KO Wild node, then an unbeatable Boss that wipes the promoted survivor so the
        // run terminates.
        var lead = Poisoner("Lead", maxHp: 160, hp: 5, speed: 100);
        // Untouched on the bench, and a Poisoner too so the scripted "poisonpowder" is a move it actually has
        // once it is promoted and has to fight the Boss (which it cannot win — poisonpowder deals no damage).
        var bench = Poisoner("Bench", maxHp: 300, hp: 300, speed: 150);
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
            // Wild foe: trades itself with the lead on the same poison tick. Boss: unbeatable — ends the run.
            var enemy =
                built == 1
                    ? Poisoner("Wild", maxHp: 160, hp: 5, speed: 1)
                    : Fighter("Boss", hp: 999, attack: 999, speed: 999);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        // Capture what the reward roll SEES. The ordering is load-bearing and otherwise untested: promotion has to
        // happen before GrantBattleRewardAsync, because PlayerCondition feeds RewardCalculator.TryRollHeal, where a
        // 0-HP lead maximises both the heal chance and its size. If promotion ever slid below the reward roll, the
        // roll would silently change and every other assertion here would still pass.
        RewardContext? rewardSaw = null;
        var draftOffers = 0;

        // Player: poisonpowder throughout (the lead has it, and so does the bench once promoted). Enemy: the wild
        // foe trades poison, then the queue advances to tackle for the Boss, which is a plain Fighter.
        var input = new ScriptedInput("poisonpowder").PicksSwitchIn(1);
        var (runner, recorder) = BuildRun(
            party,
            lead,
            supplier,
            input,
            [RunNodeKind.WildBattle, RunNodeKind.BossBattle],
            enemyInput: new ScriptedInput("poisonpowder", "tackle"),
            rewardSupplier: (rc, _) =>
            {
                rewardSaw = rc;
                return RewardChoice.None;
            },
            draftSupplier: (_, _) =>
            {
                draftOffers++;
                return Task.FromResult<Creature?>(null);
            }
        );

        await runner.RunAsync();

        // The trade counted as a win — previously the run ended here and this was never reached.
        Assert.Equal(1, runner.State.BattlesWon);
        Assert.False(lead.IsAlive());

        // The player was PROMPTED for the next lead, titled with the creature that just dropped — the forced-switch
        // prompt, reused because "your active creature fainted, someone must take over" is exactly its situation.
        var offer = Assert.Single(recorder.Of<SwitchInOffered>());
        Assert.Equal("Lead", offer.FaintedName);
        Assert.Single(input.SwitchInsOffered);

        // The result is a LEAD REASSIGNMENT, not a send-in: nobody takes the field (no enemy left), so no
        // CreatureSwitchedIn — the out-of-battle LeadChanged wire carries it, and RunState.Player is now standing.
        Assert.Empty(recorder.Of<CreatureSwitchedIn>());
        var promotion = Assert.Single(recorder.Of<LeadChanged>());
        Assert.Equal("Bench", promotion.Name);
        Assert.Same(bench, runner.State.Party.Lead);
        Assert.Same(bench, runner.State.Player);

        // The fainted lead stays on the bench at 0 HP (the next Poké Center revives it) — it is not dropped.
        Assert.Contains(lead, runner.State.Party.Members);

        // …and no carried status is written onto it, even though it was Poisoned when it went down. A corpse has
        // no ailment to carry into a next encounter it cannot enter, and skipping the write keeps this off the
        // incidental invariant that every revive path happens to clear CarriedStatus.
        Assert.Null(lead.CarriedStatus);

        // The FULL win sequence fired, not just XP (ruling 2026-07-28): the reward roll happened, and so did the
        // acquisition/draft roll.
        Assert.NotNull(rewardSaw);
        Assert.Equal(1, draftOffers);

        // …and the reward roll saw the PROMOTED SURVIVOR, not the corpse — the promotion-before-reward ordering.
        // A 0-HP condition here would mean the heal policy was being fed the fainted finisher. Compared against
        // MaxHP (the bench was untouched and full when promoted), not its live HP — the Boss has since killed it.
        Assert.Equal(bench.Attributes.MaxHP, rewardSaw!.Condition!.CurrentHp);
        Assert.True(
            rewardSaw.Condition.CurrentHp > 0,
            "the reward roll must not see a fainted lead"
        );

        // The run went on to the Boss node and only ended there, once, when the whole party was finally down.
        Assert.Equal(2, built);
        Assert.Single(recorder.Of<RunEnded>());
    }

    [Fact]
    public async Task MutualKo_APickNamingTheFaintedFinisher_IsCorrectedToTheFirstStandingMember()
    {
        // The prompt is answered over the wire, so a stale or malformed client can name slot 0 — the corpse that
        // just fainted. Promoting it would leave RunState.Player dead and end the run on the director's IsAlive
        // guard, i.e. the exact bug this whole change fixes, reachable again through a bad pick. It must be
        // corrected to the first standing member instead. Same staging as above, but the pick names the corpse.
        var lead = Poisoner("Lead", maxHp: 160, hp: 5, speed: 100);
        var bench = Poisoner("Bench", maxHp: 300, hp: 300, speed: 150);
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
            var enemy =
                built == 1
                    ? Poisoner("Wild", maxHp: 160, hp: 5, speed: 1)
                    : Fighter("Boss", hp: 999, attack: 999, speed: 999);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var input = new ScriptedInput("poisonpowder").PicksSwitchIn(0); // names the creature that just fainted
        var (runner, recorder) = BuildRun(
            party,
            lead,
            supplier,
            input,
            [RunNodeKind.WildBattle, RunNodeKind.BossBattle],
            enemyInput: new ScriptedInput("poisonpowder", "tackle")
        );

        await runner.RunAsync();

        // Corrected to the standing member — the run continued rather than ending on a dead lead.
        Assert.Equal("Bench", Assert.Single(recorder.Of<LeadChanged>()).Name);
        Assert.Same(bench, runner.State.Party.Lead);
        Assert.Equal(1, runner.State.BattlesWon);
        Assert.Equal(2, built);
    }

    [Fact]
    public async Task MutualKo_WithNoStandingBenchMember_EndsTheRun_AndNeverRaisesThePrompt()
    {
        // The other side of the mutual-KO ruling: a trade-kill is only a reprieve if SOMEONE is left standing.
        // Here the party has two members but the bench is already down, so the mutual KO takes the last creature
        // with it. PromoteSurvivorAsync must find nobody to promote, raise NO prompt (there is nothing to pick
        // from — a modal here would strand the run on an unanswerable blocking await), and let the run end.
        // Distinct from Runner_DoubleFaintFromEndOfTurnPoison_EndsTheRun_ButStillCountsTheWin, a LONE creature —
        // this pins the multi-member all-fainted-bench case, where the party is non-trivial but still wiped.
        var lead = Poisoner("Lead", maxHp: 160, hp: 5, speed: 100);
        var bench = Poisoner("Bench", maxHp: 300, hp: 300, speed: 150);
        bench.Attributes.HP = 0; // already down before this encounter
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
            return Task.FromResult(Poisoner("Wild", maxHp: 160, hp: 5, speed: 1));
        };

        var input = new ScriptedInput("poisonpowder").PicksSwitchIn(1);
        var (runner, recorder) = BuildRun(party, lead, supplier, input, [RunNodeKind.WildBattle]);

        await runner.RunAsync();

        // Both traded — and with the bench already down, the whole party is now down.
        Assert.False(lead.IsAlive());
        Assert.All(runner.State.Party.Members, m => Assert.False(m.IsAlive()));

        // No promotion prompt was raised, and no lead was reassigned onto a corpse.
        Assert.Empty(recorder.Of<SwitchInOffered>());
        Assert.Empty(input.SwitchInsOffered);
        Assert.Empty(recorder.Of<LeadChanged>());

        // The run is over — exactly once, and it did not advance to a further encounter. The trade-kill is still
        // counted, though: a mutual KO is a win, and the run ending doesn't unmake the one that ended it.
        var runEnded = Assert.Single(recorder.Of<RunEnded>());
        Assert.Equal(1, runEnded.BattlesWon);
        Assert.Equal(1, built);
    }

    // A biome-mode run over a dead-end solo biome whose route is the given node plan, so the tests control exactly
    // which encounters fire. Mirrors RunDirectorLeadChoiceTests.BuildBoundaryRun but with an injectable plan.
    private static (RunDirector runner, RecordingEmitter recorder) BuildRun(
        Party party,
        Creature lead,
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> supplier,
        ScriptedInput input,
        IReadOnlyList<RunNodeKind> plan,
        // ScriptedInput is strict (it throws if the named move isn't in the attacker's moveset) and holds ONE
        // move queue, so a run whose foes don't all share a moveset needs the enemy side driven separately.
        // Defaults to the player's input, which is what a uniform-moveset run wants.
        ScriptedInput? enemyInput = null,
        // Optional win-sequence policies, for the tests that assert the full win sequence fires (and what state it
        // sees). Null keeps the director's silent defaults: no reward, no draft.
        Func<RewardContext, IRandomSource, RewardChoice>? rewardSupplier = null,
        Func<DraftContext, IRandomSource, Task<Creature?>>? draftSupplier = null
    )
    {
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        var recorder = new RecordingEmitter();
        var runner = new RunDirector(
            lead,
            supplier,
            Gen1TypeChart.Instance,
            input,
            enemyInput ?? input,
            movePool: Array.Empty<Attack>(),
            new RunDirectorOptions
            {
                Emitter = recorder,
                Rules = new ScriptableRules().Deterministic(),
                Rng = new SeededRandomSource(0),
                PlayableBiomes = [solo],
                MinEventsPerBiome = plan.Count,
                MaxEventsPerBiome = plan.Count,
                NodePlanFactory = (_, _) => plan,
                Party = party,
                RewardSupplier = rewardSupplier,
                DraftSupplier = draftSupplier,
            }
        );
        return (runner, recorder);
    }

    // A 0-damage always-poisons attacker: both sides survive the attack phase and then drop to the end-of-turn
    // poison tick, which is how the mutual-KO case above is staged deterministically.
    private static Creature Poisoner(string name, int maxHp, int hp, int speed)
    {
        var c = new Creature(name)
        {
            Level = 50,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(50);
        c.Attributes.MaxHP = maxHp;
        c.Attributes.HP = hp;
        c.Attributes.Speed = speed;
        c.AddAttack(
            new Attack
            {
                Name = "poisonpowder",
                BaseDamage = 0,
                Accuracy = 100,
                AttackType = AttackType.Physical,
                PowerPointsMax = 99,
                StatusEffect = StatusCondition.Poison,
                EffectChance = 100,
            }
        );
        return c;
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
