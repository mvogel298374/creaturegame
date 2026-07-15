using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// The themed-draft acquisition channel in <see cref="RunDirector"/> (Phase 4 Stage 1c, ENCOUNTER_DESIGN.md §4):
/// after a win the injected draft supplier may offer a creature, which the player accepts (into an open slot, or
/// by swapping out a member when the roster is full) or declines. These pin the run-loop <em>plumbing</em> — the
/// blocking offer, the deposit, and that a decline is a pure sequencing no-op — with a delegate supplier (no DB);
/// the offer <em>policy</em> (cadence × n% × the fought-only pool) is <see cref="Web.DraftCalculatorTests"/>.
/// </summary>
public class RunDirectorAcquisitionTests
{
    [Fact]
    public async Task ThemedDraft_WhenOffered_AndAccepted_AddsToParty_AndAnnounces()
    {
        var input = new ScriptedInput("tackle").AcceptsAcquisition();
        var (runner, recorder) = BuildDraftRun(endAfterWins: 1, input, offersDraft: true);

        await runner.RunAsync();

        // The offer fired once (after the win), presenting the offered creature and the lone-starter party.
        var offer = Assert.Single(recorder.Of<AcquisitionOffered>());
        Assert.Equal("ThemedDraft", offer.Source);
        Assert.Equal("Draftee", offer.Name);
        Assert.False(offer.PartyFull);
        Assert.Single(offer.Party); // only the starter at offer time
        Assert.True(offer.Party[0].IsLead);

        // Accepted → the party grew 1 → 2, announced by CreatureAcquired + a fresh PartyUpdated snapshot.
        var acquired = Assert.Single(recorder.Of<CreatureAcquired>());
        Assert.False(acquired.Replaced);
        Assert.Null(acquired.ReplacedName);
        Assert.Equal("Draftee", acquired.Name);

        Assert.Equal(2, recorder.Of<PartyUpdated>().Last().Members.Count);
        Assert.Equal(2, runner.State.Party.Count);
        Assert.Contains(runner.State.Party.Members, m => m.Name == "Draftee");
    }

    [Fact]
    public async Task ThemedDraft_Declined_IsASequencingNoOp()
    {
        // A decline must add only its own AcquisitionOffered + AcquisitionDeclined events — nothing else about the
        // run may change. Compare the ordered event stream of an offered-then-declined run (with those two events
        // filtered out) against a run with no draft supplier at all: they must be identical.
        var declineInput = new ScriptedInput("tackle"); // default declines
        var (declineRunner, declineRec) = BuildDraftRun(
            endAfterWins: 3,
            declineInput,
            offersDraft: true
        );
        await declineRunner.RunAsync();

        var noDraftInput = new ScriptedInput("tackle");
        var (noDraftRunner, noDraftRec) = BuildDraftRun(
            endAfterWins: 3,
            noDraftInput,
            offersDraft: false
        );
        await noDraftRunner.RunAsync();

        var declineStream = declineRec
            .Events.Select(e => e.GetType().Name)
            .Where(n => n is not ("AcquisitionOffered" or "AcquisitionDeclined"))
            .ToList();
        var noDraftStream = noDraftRec.Events.Select(e => e.GetType().Name).ToList();
        Assert.Equal(noDraftStream, declineStream);

        // And the roster never changed: no deposit, party still just the starter, an offer+decline per win.
        Assert.Empty(declineRec.Of<CreatureAcquired>());
        Assert.Equal(3, declineRec.Of<AcquisitionOffered>().Count());
        Assert.Equal(3, declineRec.Of<AcquisitionDeclined>().Count());
        Assert.Equal(1, declineRunner.State.Party.Count);
    }

    [Fact]
    public async Task ThemedDraft_PartyFull_AcceptSwapsTheChosenMember_KeepingTheCap()
    {
        // A full roster (the Gen 1 cap of 6) → an accept must name a slot to swap out. Accept-replacing slot 1
        // (a bench member; the lead at slot 0 is never a swap target here) releases that member for the draftee,
        // and the party stays at 6.
        var lead = Fighter("Lead", hp: 300, attack: 999, speed: 100, level: 50);
        var party = new Party(lead);
        for (int i = 2; i <= Party.MaxSize; i++)
        {
            var m = Fighter($"Member{i}", hp: 100, attack: 10, speed: 10, level: 30);
            m.SpeciesId = i;
            party.Add(m);
        }
        Assert.True(party.IsFull);
        string releasedName = party.Members[1].Name;

        var input = new ScriptedInput("tackle").AcceptsAcquisitionReplacing(1);
        var (runner, recorder) = BuildDraftRun(
            endAfterWins: 1,
            input,
            offersDraft: true,
            party: party,
            player: lead
        );

        await runner.RunAsync();

        var offer = Assert.Single(recorder.Of<AcquisitionOffered>());
        Assert.True(offer.PartyFull);
        Assert.Equal(Party.MaxSize, offer.Party.Count);

        var acquired = Assert.Single(recorder.Of<CreatureAcquired>());
        Assert.True(acquired.Replaced);
        Assert.Equal(releasedName, acquired.ReplacedName);
        Assert.Equal("Draftee", acquired.Name);

        Assert.Equal(Party.MaxSize, runner.State.Party.Count); // still capped, not overfilled
        Assert.Equal("Draftee", runner.State.Party.Members[1].Name); // slot 1 is now the draftee
        Assert.DoesNotContain(runner.State.Party.Members, m => m.Name == releasedName);
    }

    [Fact]
    public async Task ThemedDraft_PartyFull_AcceptTargetingTheLead_IsRefusedAsADecline()
    {
        // Swapping the lead is a lead change — Stage 1d's between-biome flow, never this offer. An accept that
        // names the lead slot (0) on a full party is refused server-side (treated as a decline), so the active
        // creature can't be reassigned mid-chain even by a malformed / regressed client.
        var lead = Fighter("Lead", hp: 300, attack: 999, speed: 100, level: 50);
        var party = new Party(lead);
        for (int i = 2; i <= Party.MaxSize; i++)
        {
            var m = Fighter($"Member{i}", hp: 100, attack: 10, speed: 10, level: 30);
            m.SpeciesId = i;
            party.Add(m);
        }

        var input = new ScriptedInput("tackle").AcceptsAcquisitionReplacing(party.LeadIndex); // target the lead
        var (runner, recorder) = BuildDraftRun(
            endAfterWins: 1,
            input,
            offersDraft: true,
            party: party,
            player: lead
        );

        await runner.RunAsync();

        // The offer fired, but the lead-targeting accept resolved as a decline: no deposit, roster intact and the
        // lead's slot never overwritten. Asserted on the SLOT rather than Party.Lead because the run then wipes on
        // the unbeatable Bruiser and Stage 3's forced switch cycles LeadIndex across the party — but a refused
        // swap is precisely "slot 0 still holds the original creature", which SetLead (LeadIndex only, never a
        // reorder) can't disturb. That keeps the original claim exact rather than weakening it to "still a member".
        Assert.Single(recorder.Of<AcquisitionOffered>());
        Assert.Empty(recorder.Of<CreatureAcquired>());
        Assert.Single(recorder.Of<AcquisitionDeclined>());
        Assert.Equal(Party.MaxSize, runner.State.Party.Count);
        Assert.Same(lead, runner.State.Party.Members[0]); // the lead's slot was never swapped out
        Assert.DoesNotContain(runner.State.Party.Members, m => m.Name == "Draftee");
    }

    [Fact]
    public async Task ThemedDraft_NoSupplier_NeverOffers()
    {
        // The legacy chain / any run without a draft supplier never draws the offer — no acquisition events leak.
        var (runner, recorder) = BuildDraftRun(
            endAfterWins: 2,
            new ScriptedInput("tackle"),
            offersDraft: false
        );

        await runner.RunAsync();

        Assert.Empty(recorder.Of<AcquisitionOffered>());
        Assert.Empty(recorder.Of<CreatureAcquired>());
        Assert.Empty(recorder.Of<PartyUpdated>());
    }

    // --- Boss catch (post-Boss-win chance — Phase 4 Stage 2) -----------------------------------------------

    [Fact]
    public async Task BossCatch_WhenOffered_AndAccepted_AddsBossToParty_AndAnnounces()
    {
        var input = new ScriptedInput("tackle").AcceptsAcquisition();
        var (runner, recorder) = BuildBossRun(input, offersCatch: true);

        await runner.RunAsync();

        // The offer fired once, after the Boss win, tagged as the boss-catch channel with the lone-starter party.
        var offer = Assert.Single(recorder.Of<AcquisitionOffered>());
        Assert.Equal("BossCatch", offer.Source);
        Assert.Equal("Boss", offer.Name);
        Assert.False(offer.PartyFull);
        Assert.Single(offer.Party);

        // Accepted → the boss joined (party 1 → 2), announced by CreatureAcquired + a fresh PartyUpdated.
        var acquired = Assert.Single(recorder.Of<CreatureAcquired>());
        Assert.False(acquired.Replaced);
        Assert.Equal("Boss", acquired.Name);
        Assert.Equal(2, runner.State.Party.Count);
        Assert.Contains(runner.State.Party.Members, m => m.Name == "Boss");
    }

    [Fact]
    public async Task BossCatch_Declined_LeavesRosterUnchanged()
    {
        var input = new ScriptedInput("tackle"); // default declines
        var (runner, recorder) = BuildBossRun(input, offersCatch: true);

        await runner.RunAsync();

        // Exactly one offer per Boss win, and a decline is a pure no-op — no deposit, roster still the lone starter.
        Assert.Single(recorder.Of<AcquisitionOffered>());
        Assert.Single(recorder.Of<AcquisitionDeclined>());
        Assert.Empty(recorder.Of<CreatureAcquired>());
        Assert.Equal(1, runner.State.Party.Count);
    }

    [Fact]
    public async Task BossCatch_NoSupplier_NeverOffers()
    {
        // A Boss win with no boss-catch supplier draws no offer — no acquisition events leak onto the wire.
        var (runner, recorder) = BuildBossRun(new ScriptedInput("tackle"), offersCatch: false);

        await runner.RunAsync();

        Assert.Empty(recorder.Of<AcquisitionOffered>());
        Assert.Empty(recorder.Of<CreatureAcquired>());
    }

    [Fact]
    public async Task BossCatch_ChannelsAreDistinct_BossWinCatches_WildWinDrafts()
    {
        // At most one acquisition offer per win, routed by tier: the Boss win raises the boss-catch offer, and the
        // wild win ahead of it (in the same biome) raises the themed draft — never both channels on one win. Wiring
        // both suppliers proves the Boss win doesn't also draft and a wild win doesn't boss-catch.
        var input = new ScriptedInput("tackle"); // declines both — we assert the channels, not the deposits
        var (runner, recorder) = BuildBossRun(
            input,
            offersCatch: true,
            offersDraft: true,
            wildThenBoss: true
        );

        await runner.RunAsync();

        var sources = recorder.Of<AcquisitionOffered>().Select(o => o.Source).ToList();
        Assert.Equal(new[] { "ThemedDraft", "BossCatch" }, sources); // wild → draft, boss → catch, in node order
    }

    // Builds a solo-biome run whose route is a single Boss node (or a WildBattle→Boss pair when
    // <paramref name="wildThenBoss"/> is set): the first biome's boss (and optional wild) are pushovers so the
    // observed win(s) land, then the second biome's boss is unbeatable and ends the run. When
    // <paramref name="offersCatch"/> is set the boss-catch supplier always offers a fresh "Boss" copy (no n% gate
    // here — that policy is BossCatchCalculator's job); the core offer/deposit plumbing is what's under test.
    private static (RunDirector runner, RecordingEmitter recorder) BuildBossRun(
        ScriptedInput input,
        bool offersCatch,
        bool offersDraft = false,
        bool wildThenBoss = false
    )
    {
        var solo = new BiomeDefinition("solo", "Solo", Region.Kanto, [DamageType.Normal], []);
        IReadOnlyList<RunNodeKind> plan = wildThenBoss
            ? [RunNodeKind.WildBattle, RunNodeKind.BossBattle]
            : [RunNodeKind.BossBattle];
        int winnable = wildThenBoss ? 2 : 1;

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
                built <= winnable
                    ? Fighter($"Push{built}", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesId = 100 + built;
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        Func<BossCatchContext, IRandomSource, Task<Creature?>>? bossCatchSupplier = offersCatch
            ? (bctx, _) =>
            {
                // Echo the defeated boss as the single caught option (the real supplier rebuilds its species).
                var caught = Fighter(
                    "Boss",
                    hp: 120,
                    attack: 60,
                    speed: 40,
                    level: bctx.Boss.Level
                );
                caught.SpeciesId = bctx.Boss.SpeciesId;
                return Task.FromResult<Creature?>(caught);
            }
            : null;

        Func<DraftContext, IRandomSource, Task<Creature?>>? draftSupplier = offersDraft
            ? (_, _) =>
            {
                var d = Fighter("Draftee", hp: 100, attack: 50, speed: 50, level: 40);
                d.SpeciesId = 999;
                return Task.FromResult<Creature?>(d);
            }
            : null;

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
            minEventsPerBiome: plan.Count,
            maxEventsPerBiome: plan.Count,
            nodePlanFactory: (_, _) => plan,
            draftSupplier: draftSupplier,
            bossCatchSupplier: bossCatchSupplier
        );
        return (runner, recorder);
    }

    // Builds a legacy-chain run that ends after exactly <paramref name="endAfterWins"/> wins (the next foe is
    // unbeatable). When <paramref name="offersDraft"/> is set, the injected draft supplier offers a fresh
    // "Draftee" on every win — no cadence/pool gate here (that policy is the web DraftCalculator's job); the core
    // plumbing is what's under test. The supplier draws no run RNG, so a declined offer can't perturb the stream.
    private static (RunDirector runner, RecordingEmitter recorder) BuildDraftRun(
        int endAfterWins,
        ScriptedInput input,
        bool offersDraft,
        Party? party = null,
        Creature? player = null
    )
    {
        player ??= Fighter("Player", hp: 300, attack: 999, speed: 100, level: 50);
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
                built <= endAfterWins
                    ? Fighter($"Push{built}", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesId = 100 + built;
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        Func<DraftContext, IRandomSource, Task<Creature?>>? draftSupplier = offersDraft
            ? (_, _) =>
            {
                var d = Fighter("Draftee", hp: 100, attack: 50, speed: 50, level: 40);
                d.SpeciesId = 999;
                return Task.FromResult<Creature?>(d);
            }
            : null;

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
            healEveryNBattles: 0, // no Poké Center between the observed wins
            party: party,
            draftSupplier: draftSupplier
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
