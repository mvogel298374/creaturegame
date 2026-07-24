using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// The voluntary in-battle SWITCH turn-action at the <see cref="Battle"/> level (In-Combat Switching, Stage A —
/// the engine core). Distinct from the forced faint-switch (<see cref="BattleForcedSwitchTests"/>): here the
/// player chooses to swap the <em>still-alive</em> active creature for a benched one, at the cost of the turn.
/// <para>These cover the correctness-risk pieces of the engine core: the switch always resolves first and the
/// enemy's already-built move is repointed onto the creature that came in (the retarget fix); a partial-trap bind
/// blocks switching (but sleep/paralysis don't); major status persists on switch-out while volatiles reset; the
/// menu stays reachable out of PP (the Struggle-menu fix); and an illegal pick safely falls back to FIGHT.</para>
/// </summary>
public class BattleVoluntarySwitchTests
{
    [Fact]
    public async Task VoluntarySwitch_RepointsASlowerEnemyMoveOntoTheIncomingCreature()
    {
        // THE core retarget test. The player switches on turn 1; the enemy's move was built targeting the creature
        // that LEFT, but the switch resolves first, so Battle must repoint the enemy's move onto the incoming one.
        var lead = Fighter("Lead", hp: 300, attack: 40, defense: 100, speed: 200);
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 60, defense: 100, speed: 100); // slower than the lead

        var input = new ScriptedInput("tackle").TurnPlan(1); // turn 1: SWITCH to the bench (index 1)
        var recorder = new RecordingEmitter();
        var battle = NewBattle(lead, enemy, input, "tackle", party, recorder);

        await battle.StartFightAsync();

        Assert.Equal("Bench", recorder.Of<CreatureSwitchedIn>().First().Name);
        // The enemy's first hit landed on the creature that CAME IN, not the one that left.
        var firstHit = recorder.Of<DamageDealt>().First(d => d.TargetName is "Lead" or "Bench");
        Assert.Equal("Bench", firstHit.TargetName);
        // The outgoing lead was never touched — it left the field before the enemy struck.
        Assert.DoesNotContain(recorder.Of<DamageDealt>(), d => d.TargetName == "Lead");
        Assert.Equal(lead.Attributes.MaxHP, lead.Attributes.HP);
    }

    [Fact]
    public async Task VoluntarySwitch_ResolvesBeforeAFasterEnemyPriorityMove()
    {
        // A +1-priority enemy move from a FASTER enemy still can't pre-empt the switch: SwitchAction sits above
        // any move priority (7 > 1), so the incoming creature — not the one that left — takes the Quick Attack.
        var lead = Fighter("Lead", hp: 300, attack: 40, defense: 100, speed: 50); // slower than the enemy
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 60, defense: 100, speed: 100);
        enemy.AddAttack(QuickAttack());

        var input = new ScriptedInput("tackle").TurnPlan(1);
        var recorder = new RecordingEmitter();
        var battle = NewBattle(lead, enemy, input, "quickattack", party, recorder);

        await battle.StartFightAsync();

        Assert.Equal("Bench", recorder.Of<CreatureSwitchedIn>().First().Name);
        Assert.Equal(
            "Bench",
            recorder.Of<DamageDealt>().First(d => d.TargetName is "Lead" or "Bench").TargetName
        );
        Assert.DoesNotContain(recorder.Of<DamageDealt>(), d => d.TargetName == "Lead");
    }

    [Fact]
    public async Task VoluntarySwitch_WhileTrappedByABind_IsRefused()
    {
        // Gen 1: a partial-trap bind (Wrap/Bind/Clamp/Fire Spin) is the ONE thing that blocks switching. The enemy
        // binds the lead on turn 1; the lead's turn-2 switch attempt is refused (CanSwitchTo's trap gate). Without
        // that gate the SwitchAction — which isn't subject to the CanAct status gate — would execute anyway, so an
        // empty CreatureSwitchedIn is a real proof the trap blocked it.
        // The lead is tanky (survives the bind chip) but hits softly, so it does NOT one-shot the enemy on turn 1
        // — the enemy must survive to land its bind. The enemy is frail enough that the lead's intermittent free
        // turns (between binds) still whittle it down, so the battle terminates.
        var lead = Fighter("Lead", hp: 999, attack: 60, defense: 999, speed: 200);
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 40, attack: 1, defense: 100, speed: 100);
        enemy.AddAttack(Wrap());

        // Turn 1: FIGHT (the enemy binds the lead). Turn 2: attempt to SWITCH — refused while trapped.
        var input = new ScriptedInput("tackle").TurnPlan(null, 1);
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("wrap"),
            rules: new ScriptableRules().Deterministic().BindingTurns(3),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerParty: party
        );

        await battle.StartFightAsync();

        Assert.Contains(recorder.Of<BindingBlocked>(), b => b.CreatureName == "Lead");
        Assert.Empty(recorder.Of<CreatureSwitchedIn>()); // the trapped switch never fired
        Assert.Same(lead, party.Lead);
        Assert.Equal("Lead", recorder.Of<BattleEnded>().Last().WinnerName); // freed itself and won
    }

    [Fact]
    public async Task VoluntarySwitch_PersistsMajorStatusOntoTheOutgoing_AndResetsVolatiles_OnSwitchBackIn()
    {
        // Gen 1: switching out keeps major status on the creature but wipes volatiles. The lead raises its Attack
        // (a volatile) and is poisoned (major) on turn 1, switches out on turn 2, and back in on turn 3 — it must
        // return poisoned with its Attack stage reset to 0.
        var lead = Fighter("Lead", hp: 999, attack: 40, defense: 999, speed: 200);
        lead.AddAttack(SwordsDance());
        var bench = Fighter("Bench", hp: 999, attack: 999, defense: 999, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 60, attack: 1, defense: 999, speed: 100);
        enemy.AddAttack(PoisonPowder());

        // Turn 1: FIGHT (Swords Dance → Attack +2, and the enemy poisons the lead). Turn 2: SWITCH to bench.
        // Turn 3: SWITCH back to the lead. Then FIGHT "tackle" until the frail enemy drops.
        var input = new ScriptedInput("swordsdance", "tackle").TurnPlan(null, 1, 0);
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("poisonpowder"),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerParty: party
        );

        await battle.StartFightAsync();

        // Captured on switch-out: the lead's own carried status is now Poison (Gen 1 keeps it out of battle).
        Assert.Equal(StatusCondition.Poison, lead.CarriedStatus?.Status);
        // Restored on switch-back-in: the SECOND switch-in (the lead returning) enters poisoned.
        var switchIns = recorder.Of<CreatureSwitchedIn>().ToList();
        Assert.Equal("Bench", switchIns[0].Name);
        Assert.Equal("Lead", switchIns[1].Name);
        Assert.Equal(StatusCondition.Poison, switchIns[1].Status);
        // Volatiles reset: the lead really raised its Attack (+2), and it is back to 0 after the switch-back-in.
        Assert.Contains(
            recorder.Of<StatStageChanged>(),
            s => s.CreatureName == "Lead" && s.Stat == nameof(StageStat.Attack) && s.NewStage == 2
        );
        Assert.Equal(0, lead.Battle.Stages.Attack);
    }

    [Fact]
    public async Task VoluntarySwitch_OfABadlyPoisonedCreature_DowngradesToRegularPoison()
    {
        // Gen 1: the Toxic escalation is a battle-only volatile, so switching out reverts Bad Poison to regular
        // Poison (a known stalling counter-play). The lead enters Bad Poison, switches out (turn 1) and back in
        // (turn 2) — it must return as regular Poison, not Bad Poison. (Adjudicated with the user 2026-07-24: the
        // code was right; GEN_DIFFERENCES.md's old "switching does not reset the counter" line was the error.)
        var lead = Fighter("Lead", hp: 999, attack: 999, defense: 999, speed: 200);
        var bench = Fighter("Bench", hp: 999, attack: 100, defense: 999, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 60, attack: 1, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").TurnPlan(1, 0); // switch out, then back in
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerEntryStatus: new CarriedStatus(StatusCondition.BadPoison, 0),
            playerParty: party
        );

        await battle.StartFightAsync();

        // Downgraded on switch-out, and it returns as regular Poison — not Bad Poison.
        Assert.Equal(StatusCondition.Poison, lead.CarriedStatus?.Status);
        var switchIns = recorder.Of<CreatureSwitchedIn>().ToList();
        Assert.Equal("Lead", switchIns[1].Name);
        Assert.Equal(StatusCondition.Poison, switchIns[1].Status);
    }

    [Fact]
    public async Task Rage_BlocksSwitchingLikeAnyOtherLockIn()
    {
        // Adjudicated with the user 2026-07-24: Rage is treated as a true lock-in (like Thrash) — its whole-turn
        // menu is bypassed, so SWITCH is unreachable while raging. Guards that the Struggle-menu fix didn't open
        // the menu for a raging creature. The lead locks into Rage on turn 1; the turn-2 SWITCH is never consulted.
        var lead = Fighter("Lead", hp: 300, attack: 200, defense: 100, speed: 200);
        lead.AddAttack(Rage());
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 60, attack: 1, defense: 100, speed: 100);

        var input = new ScriptedInput("rage").TurnPlan(null, 1); // turn 1 FIGHT Rage; turn 2 SWITCH (ignored)
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerParty: party
        );

        await battle.StartFightAsync();

        Assert.Empty(recorder.Of<CreatureSwitchedIn>()); // the raging creature never switched
        Assert.True(recorder.Of<MoveUsed>().Count(m => m.MoveName == "rage") >= 2); // kept raging
    }

    [Fact]
    public async Task VoluntarySwitch_DuringHyperBeamRecharge_IsAllowed()
    {
        // Adjudicated with the user 2026-07-24: switching out during a Hyper Beam recharge turn is legal Gen 1 —
        // the recharge is enforced inside AttackAction (only spent if the creature stays in and FIGHTs), not the
        // turn menu, so CanSwitchTo does not gate on IsRecharging. The lead fires Hyper Beam (recharge set) then
        // switches out on the recharge turn, pre-empting the wasted turn (no Recharging event ever fires).
        var lead = Fighter("Lead", hp: 300, attack: 40, defense: 999, speed: 200);
        lead.AddAttack(HyperBeam());
        var bench = Fighter("Bench", hp: 999, attack: 999, defense: 999, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 300, attack: 40, defense: 100, speed: 100);

        // turn 1 Hyper Beam; turn 2 SWITCH mid-recharge; the bench then FIGHTs with its own move ("tackle").
        var input = new ScriptedInput("hyperbeam", "tackle").TurnPlan(null, 1);
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerParty: party
        );

        await battle.StartFightAsync();

        Assert.Contains(
            recorder.Of<MoveUsed>(),
            m => m.AttackerName == "Lead" && m.MoveName == "hyperbeam"
        );
        Assert.Equal("Bench", recorder.Of<CreatureSwitchedIn>().First().Name); // switched despite recharging
        Assert.Empty(recorder.Of<Recharging>()); // it never spent the recharge turn — the switch pre-empted it
    }

    [Fact]
    public async Task VoluntarySwitch_IsReachableOutOfPP_NotForcedToStruggle()
    {
        // The Struggle-menu fix: Gen 1 keeps the menu open out of PP, so BAG/SWITCH stay reachable and only
        // *choosing FIGHT* Struggles. With every move at 0 PP the lead still switches instead of being force-Struggled.
        var lead = Fighter("Lead", hp: 300, attack: 40, defense: 100, speed: 200);
        foreach (var m in lead.MoveSet)
            m.PowerPointsCurrent = 0;
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 40, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").TurnPlan(1); // SWITCH despite 0 PP everywhere
        var recorder = new RecordingEmitter();
        var battle = NewBattle(lead, enemy, input, "tackle", party, recorder);

        await battle.StartFightAsync();

        Assert.Equal("Bench", recorder.Of<CreatureSwitchedIn>().First().Name);
        // The lead never acted at all — it switched out, it did NOT Struggle.
        Assert.DoesNotContain(recorder.Of<MoveUsed>(), m => m.AttackerName == "Lead");
    }

    [Fact]
    public async Task OutOfPP_ChoosingFight_StillResolvesToStruggle()
    {
        // The other half of the Struggle-menu fix: offering the menu out of PP must not have broken Struggle
        // itself. With no switch/bag chosen (a plain FIGHT default), a moveless creature still Struggles.
        var player = Fighter("Player", hp: 300, attack: 40, defense: 100, speed: 200);
        foreach (var m in player.MoveSet)
            m.PowerPointsCurrent = 0;
        var enemy = Fighter("Foe", hp: 40, attack: 1, defense: 100, speed: 100);

        var recorder = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"), // no TurnPlan → FIGHT; 0 PP → Struggle
            new ScriptedInput("tackle"),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0)
        );

        await battle.StartFightAsync();

        Assert.Contains(
            recorder.Of<MoveUsed>(),
            m => m.AttackerName == "Player" && m.MoveName == "Struggle"
        );
    }

    [Theory]
    [InlineData(0)] // the already-active member
    [InlineData(99)] // past the end of the roster
    [InlineData(-1)] // negative
    public async Task VoluntarySwitch_IllegalPick_FallsBackToFight(int pick)
    {
        // A stale / malformed pick (the active slot, or out of range in either direction) must not strand the
        // turn: it falls back to FIGHT rather than switching. The lead one-shots the frail enemy instead.
        var lead = Fighter("Lead", hp: 300, attack: 999, defense: 100, speed: 200);
        var bench = Fighter("Bench", hp: 300, attack: 100, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 60, attack: 1, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").TurnPlan(pick);
        var recorder = new RecordingEmitter();
        var battle = NewBattle(lead, enemy, input, "tackle", party, recorder);

        await battle.StartFightAsync();

        Assert.Empty(recorder.Of<CreatureSwitchedIn>());
        Assert.Contains(recorder.Of<MoveUsed>(), m => m.AttackerName == "Lead"); // fought instead
        Assert.Same(lead, party.Lead);
    }

    [Fact]
    public async Task VoluntarySwitch_ToAFaintedBenchSlot_FallsBackToFight()
    {
        // The in-range-but-fainted branch of the illegal-pick guard: picking a downed member is refused (you can't
        // send in a fainted creature voluntarily), so the turn falls back to FIGHT.
        var lead = Fighter("Lead", hp: 300, attack: 999, defense: 100, speed: 200);
        var downed = Fighter("Downed", hp: 300, attack: 100, defense: 100, speed: 150);
        downed.Attributes.HP = 0;
        var party = new Party(lead);
        party.Add(downed);
        var enemy = Fighter("Foe", hp: 60, attack: 1, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").TurnPlan(1); // slot 1 is fainted
        var recorder = new RecordingEmitter();
        var battle = NewBattle(lead, enemy, input, "tackle", party, recorder);

        await battle.StartFightAsync();

        Assert.Empty(recorder.Of<CreatureSwitchedIn>());
        Assert.Contains(recorder.Of<MoveUsed>(), m => m.AttackerName == "Lead");
    }

    [Fact]
    public async Task TrueLockIn_BypassesTheMenu_SoASwitchCannotBeChosen()
    {
        // A true lock-in (Thrash/rampage) owns the whole turn — no menu at all — so even a scripted SWITCH on the
        // locked turn is never consulted. This guards that the Struggle-menu fix did NOT accidentally open the menu
        // for a locked-in creature (which stays force-repeating, unlike the out-of-PP case).
        var lead = Fighter("Lead", hp: 300, attack: 300, defense: 100, speed: 200);
        lead.AddAttack(Thrash());
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 90, attack: 1, defense: 100, speed: 100);

        // Turn 1: FIGHT Thrash (locks in). Turn 2: a SWITCH is scripted but must be ignored (still locked).
        var input = new ScriptedInput("thrash").TurnPlan(null, 1);
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            rules: new ScriptableRules().Deterministic().RampageTurns(3),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerParty: party
        );

        await battle.StartFightAsync();

        Assert.Empty(recorder.Of<CreatureSwitchedIn>()); // the locked-in creature never switched
        // It kept thrashing across turns 1 and 2 (the menu — and thus SWITCH — was bypassed both times).
        Assert.True(recorder.Of<MoveUsed>().Count(m => m.MoveName == "thrash") >= 2);
    }

    [Theory]
    [InlineData(StatusCondition.Sleep)]
    [InlineData(StatusCondition.Paralysis)]
    [InlineData(StatusCondition.Freeze)]
    public async Task VoluntarySwitch_WhileAfflictedButNotTrapped_StillExecutes(
        StatusCondition status
    )
    {
        // Gen 1: only a partial-trap bind blocks switching — sleep / paralysis / freeze / confusion do NOT (that
        // is the whole point of switching as a defensive option). SwitchAction is deliberately NOT subject to the
        // CanAct status gate, so an afflicted-but-untrapped creature still switches. This pins that positively, so
        // a later "reuse CanAct on SwitchAction for consistency" refactor can't silently reintroduce the bug.
        var lead = Fighter("Lead", hp: 300, attack: 40, defense: 100, speed: 200);
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 40, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").TurnPlan(1);
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            // The opening lead enters afflicted (its ctor entry status), then tries to switch on turn 1.
            playerEntryStatus: new CarriedStatus(status, status == StatusCondition.Sleep ? 3 : 0),
            playerParty: party
        );

        await battle.StartFightAsync();

        Assert.Equal("Bench", recorder.Of<CreatureSwitchedIn>().First().Name);
    }

    [Fact]
    public async Task VoluntarySwitch_IncomingFaintsToTheSameTurnsHit_ThenTheForcedPathTakesOver()
    {
        // The incoming creature can be KO'd by the same turn's (retargeted) enemy hit. There is NO recursive
        // switch prompt inside the voluntary switch — the faint is handled by the FORCED path at end of turn, so
        // the only SwitchInOffered is that forced one.
        var lead = Fighter("Lead", hp: 300, attack: 40, defense: 100, speed: 200);
        var frail = Fighter("Frail", hp: 10, attack: 40, defense: 1, speed: 150); // dies to the enemy hit
        var anchor = Fighter("Anchor", hp: 400, attack: 999, defense: 200, speed: 150);
        var party = new Party(lead);
        party.Add(frail);
        party.Add(anchor);
        var enemy = Fighter("Foe", hp: 500, attack: 999, defense: 100, speed: 100);

        // Voluntary switch (turn 1) sends in Frail; the forced switch after its faint sends in Anchor (index 2).
        var input = new ScriptedInput("tackle").TurnPlan(1).PicksSwitchIn(2);
        var recorder = new RecordingEmitter();
        var battle = NewBattle(lead, enemy, input, "tackle", party, recorder);

        await battle.StartFightAsync();

        var switchIns = recorder.Of<CreatureSwitchedIn>().ToList();
        Assert.Equal("Frail", switchIns[0].Name); // the voluntary switch-in
        Assert.Contains(recorder.Of<CreatureFainted>(), f => f.Name == "Frail");
        Assert.Single(recorder.Of<SwitchInOffered>()); // ONLY the forced prompt — the voluntary switch raised none
        Assert.Equal("Anchor", switchIns[1].Name); // the forced send-in took over
    }

    // A party-aware battle with deterministic rules; the enemy spams a single named move.
    private static Battle NewBattle(
        Creature lead,
        Creature enemy,
        ScriptedInput playerInput,
        string enemyMove,
        Party party,
        RecordingEmitter recorder
    ) =>
        new(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            playerInput,
            new ScriptedInput(enemyMove),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerParty: party
        );

    private static Attack QuickAttack() =>
        new()
        {
            Id = 98,
            Name = "quickattack",
            BaseDamage = 40,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            Priority = 1,
        };

    private static Attack Wrap() =>
        new()
        {
            Id = 35,
            Name = "wrap",
            BaseDamage = 15,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            Effect = MoveEffect.Binding,
        };

    private static Attack PoisonPowder() =>
        new()
        {
            Id = 77,
            Name = "poisonpowder",
            BaseDamage = 0,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            StatusEffect = StatusCondition.Poison,
            EffectChance = 100,
        };

    private static Attack SwordsDance() =>
        new()
        {
            Id = 14,
            Name = "swordsdance",
            BaseDamage = 0,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            StatEffectStat = StageStat.Attack,
            StatEffectDelta = 2,
            StatEffectTarget = StageTarget.Self,
            StatEffectChance = 100,
        };

    private static Attack Thrash() =>
        new()
        {
            Id = 37,
            Name = "thrash",
            BaseDamage = 40,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            Effect = MoveEffect.Rampage,
        };

    private static Attack Rage() =>
        new()
        {
            Id = 99,
            Name = "rage",
            BaseDamage = 20,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            Effect = MoveEffect.Rage,
        };

    // Physical (not Special) purely so the test's damage is predictable from the stats set here; the recharge
    // behaviour under test is category-independent. The enemy must survive the hit for the recharge flag to set.
    private static Attack HyperBeam() =>
        new()
        {
            Id = 63,
            Name = "hyperbeam",
            BaseDamage = 150,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            Effect = MoveEffect.Recharge,
        };

    private static Creature Fighter(string name, int hp, int attack, int defense, int speed)
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
        c.Attributes.Defense = defense;
        c.Attributes.Speed = speed;
        c.AddAttack(
            new Attack
            {
                Id = 33,
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
