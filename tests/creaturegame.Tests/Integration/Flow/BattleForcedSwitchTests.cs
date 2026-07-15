using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.Integration.Gen1Attacks;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// Forced switch-on-faint at the <see cref="Battle"/> level (Encounter Logic Phase 4 Stage 3): when the active
/// player creature faints but a bench member is still alive, <see cref="Battle"/> sends in a replacement against
/// the <em>same</em> enemy instead of ending — the run ends only when the whole party is down. Party-aware only
/// (a <see cref="Party"/> is wired); the legacy single-creature battle (no party) still ends on the lead's faint.
/// Gen 1 fidelity: the incoming creature resets its volatiles but keeps its own carried major status, and the
/// enemy is untouched by the switch (its HP/state carry across).
/// </summary>
public class BattleForcedSwitchTests
{
    [Fact]
    public async Task ActiveFaints_WithLiveBench_SendsInTheChosenMember_AndTheEnemyKeepsItsDamage()
    {
        // Lead is fast + frail: it lands one hit on the enemy, then the enemy KOs it. A live bench member is then
        // sent in against the SAME (already-damaged) enemy and finishes the job — proving the run continues and
        // the enemy's HP carried across the switch (it was not reset).
        var lead = Fighter("Lead", hp: 10, attack: 100, defense: 100, speed: 200);
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 999, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").PicksSwitchIn(1);
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

        // The lead fainted and the forced switch-in fired exactly once, offering the roster with the fainted lead.
        Assert.Single(input.SwitchInsOffered);
        Assert.Contains(recorder.Of<CreatureFainted>(), f => f.Name == "Lead");
        var switchIn = Assert.Single(recorder.Of<CreatureSwitchedIn>());
        Assert.Equal("Bench", switchIn.Name);
        // The run continued to a win by the switched-in creature (the enemy is what fainted last).
        Assert.Equal("Bench", recorder.Of<BattleEnded>().Last().WinnerName);
        Assert.False(enemy.IsAlive());

        // Enemy-state preservation: the enemy carried its damage across the switch. The last hit BEFORE the
        // switch (the lead's) left it below full, and the first hit AFTER the switch (the bench's) drove it lower
        // still — the enemy was never reset to full HP by the send-in.
        var events = recorder.Events.ToList();
        int switchIdx = events.FindIndex(e => e is CreatureSwitchedIn);
        var enemyHits = events
            .Select((e, i) => (e, i))
            .Where(x => x.e is DamageDealt d && d.TargetName == "Foe")
            .ToList();
        int preHp = ((DamageDealt)enemyHits.Last(x => x.i < switchIdx).e).HpAfter;
        int postHp = ((DamageDealt)enemyHits.First(x => x.i > switchIdx).e).HpAfter;
        Assert.True(
            preHp < enemy.Attributes.MaxHP,
            "lead should have damaged the enemy before fainting"
        );
        Assert.True(
            postHp < preHp,
            $"enemy HP should keep dropping across the switch (pre {preHp} → post {postHp}), not reset to full"
        );
    }

    [Fact]
    public async Task ActiveFaints_WithNoLiveBenchMember_EndsTheBattleAsALoss()
    {
        // A two-member party whose bench is already fainted: the lead's faint has no one to switch to, so the
        // battle ends as a loss (the whole party is down) — no switch is offered.
        var lead = Fighter("Lead", hp: 10, attack: 1, defense: 100, speed: 50);
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        bench.Attributes.HP = 0; // already down
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 999, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle");
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

        Assert.Empty(input.SwitchInsOffered);
        Assert.Empty(recorder.Of<CreatureSwitchedIn>());
        Assert.Equal("Foe", recorder.Of<BattleEnded>().Last().WinnerName);
    }

    [Fact]
    public async Task SingleCreatureBattle_NoPartyWired_FaintEndsTheBattle_LegacyUnchanged()
    {
        // No party → the legacy single-creature battle: the lead's faint ends the battle with no switch attempt,
        // exactly as before Stage 3 (a regression guard for every existing direct-Battle caller).
        var lead = Fighter("Lead", hp: 10, attack: 1, defense: 100, speed: 50);
        var enemy = Fighter("Foe", hp: 500, attack: 999, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle");
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0)
        // no playerParty
        );

        await battle.StartFightAsync();

        Assert.Empty(input.SwitchInsOffered);
        Assert.Empty(recorder.Of<CreatureSwitchedIn>());
        Assert.Equal("Foe", recorder.Of<BattleEnded>().Last().WinnerName);
    }

    [Fact]
    public async Task SwitchIn_IncomingKeepsItsOwnCarriedStatus_AndTheOutgoingsStatusNeverLeaks()
    {
        // The multi-creature carry model (STATE_MODEL.md §2): the incoming creature enters on ITS OWN out-of-battle
        // status. The fainted lead carries Sleep, the bench carries Poison — the send-in must surface Poison (the
        // bench's own), never the lead's Sleep. The CreatureSwitchedIn event carries the applied entry status.
        var lead = Fighter("Lead", hp: 10, attack: 100, defense: 100, speed: 200);
        lead.CarriedStatus = new CarriedStatus(StatusCondition.Sleep, 3);
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        bench.CarriedStatus = new CarriedStatus(StatusCondition.Poison, 0);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 999, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").PicksSwitchIn(1);
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

        var switchIn = Assert.Single(recorder.Of<CreatureSwitchedIn>());
        Assert.Equal("Bench", switchIn.Name);
        Assert.Equal(StatusCondition.Poison, switchIn.Status); // its own carried status — the lead's Sleep did NOT leak
    }

    [Fact]
    public async Task ForcedSwitch_StaleOrFaintedPick_FallsBackToTheFirstLiveMember()
    {
        // A malformed / stale index (a fainted slot, or out of range) must never send in a downed creature: Battle
        // corrects it to the first live member. Slot 1's bench is fainted; the pick names it, but slot 2's live
        // member is sent in instead.
        var lead = Fighter("Lead", hp: 10, attack: 100, defense: 100, speed: 200);
        var downed = Fighter("Downed", hp: 300, attack: 999, defense: 100, speed: 150);
        downed.Attributes.HP = 0;
        var live = Fighter("Live", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(downed);
        party.Add(live);
        var enemy = Fighter("Foe", hp: 500, attack: 999, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").PicksSwitchIn(1); // names the FAINTED bench slot
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

        var switchIn = Assert.Single(recorder.Of<CreatureSwitchedIn>());
        Assert.Equal("Live", switchIn.Name); // corrected to the first LIVE member, not the fainted slot 1
        Assert.Same(live, party.Lead);
    }

    [Theory]
    [InlineData(-1)] // negative
    [InlineData(99)] // past the end of the roster
    public async Task ForcedSwitch_OutOfRangePick_FallsBackToTheFirstLiveMember(int pick)
    {
        // The hub forwards whatever int the client sends (a stale or malformed pick), so an index outside the
        // roster — in EITHER direction — must be corrected to the first live member rather than throw or strand
        // the run. The sibling test covers the third fallback branch (an in-range but fainted slot).
        var lead = Fighter("Lead", hp: 10, attack: 100, defense: 100, speed: 200);
        var live = Fighter("Live", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(live);
        var enemy = Fighter("Foe", hp: 500, attack: 999, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle").PicksSwitchIn(pick);
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

        var switchIn = Assert.Single(recorder.Of<CreatureSwitchedIn>());
        Assert.Equal("Live", switchIn.Name);
        Assert.Same(live, party.Lead);
    }

    [Fact]
    public async Task DoubleFaint_WithALiveBenchMember_OffersNoSwitch_AndKeepsTheLossSemantics()
    {
        // The enemy-faint (win) check runs BEFORE the player-faint/switch check, so the forced switch only ever
        // fires on the isolated path "enemy alive + active fainted". A simultaneous double faint must therefore
        // keep its pre-Stage-3 semantics even now that a party is wired: no switch is offered (a mutual KO does
        // not buy the run a free continue off the bench) and the battle still resolves as a loss. The existing
        // double-faint regression runs on a null-party battle, which never reaches this branch at all.
        // maxHP 160 → poison tick = 10; HP 5 → the first tick is lethal to both. Both use a 0-damage poison
        // move, so the attack phase changes nothing and both are alive (poisoned) entering end-of-turn.
        var lead = Poisoner("Lead", maxHp: 160, hp: 5, speed: 100);
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150); // alive on the bench
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Poisoner("Foe", maxHp: 160, hp: 5, speed: 1);

        var input = new ScriptedInput("poisonpowder");
        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("poisonpowder"),
            rules: new ScriptableRules().Deterministic(), // alwaysHit so each status move lands
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerParty: party
        );

        await battle.StartFightAsync();

        // Both dropped to a poison tick on the same end-of-turn.
        Assert.False(lead.IsAlive());
        Assert.False(enemy.IsAlive());
        Assert.Equal(2, recorder.Of<StatusDamage>().Count(d => d.Source == StatusCondition.Poison));

        // The live bench member was never offered, never sent in, and never became the lead.
        Assert.True(bench.IsAlive());
        Assert.Empty(input.SwitchInsOffered);
        Assert.Empty(recorder.Of<SwitchInOffered>());
        Assert.Empty(recorder.Of<CreatureSwitchedIn>());
        Assert.Same(lead, party.Lead);

        // Unchanged loss semantics: the fainted player is not the winner despite the enemy also dropping.
        Assert.Equal("Foe", recorder.Of<BattleEnded>().Last().WinnerName);
    }

    [Fact]
    public async Task SwitchIn_IncomingNeitherActsNorTakesEndOfTurnDamage_OnTheTurnItEnters()
    {
        // Canonical Gen 1: the replacement enters after the turn has ALREADY resolved — so on its entry turn it
        // does not act, takes no end-of-turn tick (it arrives on full HP despite carrying Poison), and the enemy
        // gets no free hit at it. Its first poison tick lands only at the end of its first real turn.
        var lead = Fighter("Lead", hp: 10, attack: 1, defense: 1, speed: 200);
        var bench = Fighter("Bench", hp: 320, attack: 60, defense: 200, speed: 150);
        bench.CarriedStatus = new CarriedStatus(StatusCondition.Poison, 0);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 200, defense: 100, speed: 100);

        var input = new ScriptedInput("tackle");
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

        var events = recorder.Events.ToList();
        int switchIdx = events.FindIndex(e => e is CreatureSwitchedIn);
        Assert.True(switchIdx >= 0, "the forced switch must have fired");

        // It entered on FULL HP carrying its OWN Poison: the entry turn's end-of-turn damage was already past.
        var switchIn = (CreatureSwitchedIn)events[switchIdx];
        Assert.Equal(StatusCondition.Poison, switchIn.Status);
        Assert.Equal(switchIn.MaxHp, switchIn.Hp);

        // Nothing happens to or by the incoming creature between the send-in and the next turn boundary.
        int nextTurnIdx = events.FindIndex(switchIdx + 1, e => e is TurnStarted);
        Assert.True(nextTurnIdx > switchIdx, "a new turn must begin after the send-in");
        var entryWindow = events.GetRange(switchIdx + 1, nextTurnIdx - switchIdx - 1);
        Assert.DoesNotContain(entryWindow, e => e is MoveUsed m && m.AttackerName == "Bench");
        Assert.DoesNotContain(entryWindow, e => e is StatusDamage s && s.TargetName == "Bench");
        Assert.DoesNotContain(entryWindow, e => e is DamageDealt d && d.TargetName == "Bench");

        // The first tick it does take comes after its first FULL turn, not on entry.
        int firstTick = events.FindIndex(e => e is StatusDamage s && s.TargetName == "Bench");
        Assert.True(
            firstTick > nextTurnIdx,
            "the incoming creature's first poison tick must land at the end of its first real turn, not on entry"
        );
    }

    [Fact]
    public async Task ActiveFaints_OnTheSameTurnAFoeFled_EndsTheBattle_WithoutSwitchingOrGivingTheFoeAFreeTurn()
    {
        // Roar/Whirlwind scares the foe off the field. If the active creature then faints to end-of-turn poison
        // on that SAME turn, there is no longer anyone to send a bench member in against — so the forced switch
        // must NOT fire. It's gated on the flee for a second reason: a switch-in `continue`s past the end-of-turn
        // flee gate, so without the gate the already-fled foe would get a free turn against the incoming creature
        // before the battle finally ended. The documented ordering holds — "a faint takes precedence (a KO is a
        // real result)" — so this ends as a loss, and the run ends with it.
        var lead = Fighter("Lead", hp: 160, attack: 1, defense: 999, speed: 200);
        lead.Attributes.HP = 5; // maxHP stays 160 → the 10-point poison tick is lethal this turn
        lead.AddAttack(
            new Attack
            {
                Id = 46, // distinct from Fighter's tackle (id 0) — AddAttack dedupes on Id
                Name = "roar",
                BaseDamage = 0,
                Accuracy = 100,
                AttackType = AttackType.Undefined,
                PowerPointsMax = 99,
                Effect = MoveEffect.ForceFlee,
            }
        );
        var bench = Fighter("Bench", hp: 300, attack: 999, defense: 100, speed: 150);
        var party = new Party(lead);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 500, attack: 1, defense: 100, speed: 100);

        // "tackle" is scripted after the roar purely so that a REGRESSION (the switch firing) fails on the
        // assertions below rather than blowing up on the bench having no "roar" — the bench must never be asked.
        var input = new ScriptedInput("roar", "tackle");
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
            // The opening lead's entry status comes from the ctor (the same value RunDirector threads from
            // player.CarriedStatus) — the per-member CarriedStatus field only drives a switch-IN.
            playerEntryStatus: new CarriedStatus(StatusCondition.Poison, 0),
            escapable: true, // a wild battle — Roar lands
            playerParty: party
        );

        await battle.StartFightAsync();

        // The foe really was scared off and the lead really did faint — otherwise this passes vacuously.
        Assert.True(enemy.Battle.HasFled, "Roar must have scared the foe off this turn");
        Assert.Contains(recorder.Of<CreatureFainted>(), f => f.Name == "Lead");

        // The faint takes precedence over the flee ("a KO is a real result"), so the battle ends as a LOSS and
        // never announces the flee — the run reads a loss, not a no-result advance.
        Assert.False(battle.EndedInFlee);
        Assert.Empty(recorder.Of<CreatureFled>());
        Assert.Equal("Foe", recorder.Of<BattleEnded>().Last().WinnerName);

        // No switch, despite a perfectly healthy bench member being available.
        Assert.True(bench.IsAlive());
        Assert.Empty(input.SwitchInsOffered);
        Assert.Empty(recorder.Of<CreatureSwitchedIn>());
        Assert.Same(lead, party.Lead);

        // And the fled foe never got a free turn at anyone: the battle stopped on the turn it fled.
        Assert.Single(recorder.Of<TurnStarted>());
    }

    // A 0-damage always-poisons move: the attack phase changes no HP, so both sides enter end-of-turn alive and
    // poisoned (the same shape the existing double-faint regression uses).
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

/// <summary>
/// The forced faint-switch crossed with the identity-mutating moves (Transform / Mimic), which change the
/// <em>permanent</em> half of a <see cref="Creature"/> — moveset, types, SpeciesId, the four non-HP stats.
/// <see cref="Battle"/>'s end-of-battle restore only ever reaches whichever creature is active at that point, so
/// a creature that mutates and then faints into a forced switch must be reverted <em>as it leaves the field</em>
/// or the copy leaks onto the bench for the rest of the run. Driven through the real moves DB, like the other
/// Transform contracts.
/// </summary>
[Collection(MovesCollection.Name)]
public class BattleForcedSwitchIdentityTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task ForcedSwitch_TransformedOutgoingCreature_IsRestoredBeforeItLeavesTheField()
    {
        // The lead outspeeds and Transforms into the foe (copying its type, SpeciesId, non-HP stats and
        // moveset), then is KO'd by the foe the same turn. The bench comes in, so the lead is never the active
        // creature again and the end-of-battle restore can never reach it — only TrySwitchInAsync's
        // revert-on-the-way-out keeps its permanent half clean.
        var lead = Mon(
            "Lead",
            hp: 10,
            attack: 100,
            defense: 100,
            speed: 200,
            type: DamageType.Normal
        );
        lead.SpeciesId = 132; // Ditto
        lead.AddAttack(Move("transform"));
        var bench = Mon(
            "Bench",
            hp: 300,
            attack: 999,
            defense: 100,
            speed: 150,
            type: DamageType.Normal
        );
        bench.AddAttack(Tackle());
        var enemy = Mon(
            "Foe",
            hp: 500,
            attack: 999,
            defense: 250,
            speed: 100,
            type: DamageType.Water
        );
        enemy.SpeciesId = 134; // Vaporeon
        enemy.AddAttack(Tackle());

        var party = new Party(lead);
        party.Add(bench);

        var recorder = new RecordingEmitter();
        var battle = new Battle(
            lead,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput("transform", "tackle"), // lead Transforms turn 1; the bench then attacks
            new ScriptedInput("tackle"),
            rules: new ScriptableRules().Deterministic(),
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerParty: party
        );

        await battle.StartFightAsync();

        // The mutation really happened and the lead really fainted into a real switch — without these the
        // restore assertions below would pass vacuously.
        Assert.Single(recorder.Of<TransformedInto>());
        Assert.Contains(recorder.Of<CreatureFainted>(), f => f.Name == "Lead");
        Assert.Equal("Bench", Assert.Single(recorder.Of<CreatureSwitchedIn>()).Name);

        // The benched, fainted lead is its own creature again: nothing of the foe's copied identity stuck.
        Assert.Null(lead.Battle.OriginalIdentity);
        Assert.Equal(DamageType.Normal, lead.Type1);
        Assert.Equal(132, lead.SpeciesId);
        Assert.Equal(100, lead.Attributes.Defense);
        Assert.Equal(200, lead.Attributes.Speed);
        var kept = Assert.Single(lead.MoveSet);
        Assert.Equal("transform", kept.Base.Name);
        Assert.DoesNotContain(lead.MoveSet, m => m.Base.Name == "tackle");
    }

    private static Attack Tackle() =>
        new()
        {
            Name = "tackle",
            BaseDamage = 40,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
        };

    private static Creature Mon(
        string name,
        int hp,
        int attack,
        int defense,
        int speed,
        DamageType type
    )
    {
        var c = new Creature(name)
        {
            Level = 50,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = type,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(50);
        c.Attributes.MaxHP = hp;
        c.Attributes.HP = hp;
        c.Attributes.Attack = attack;
        c.Attributes.Defense = defense;
        c.Attributes.Speed = speed;
        return c;
    }
}
