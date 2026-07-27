using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// How a win's XP is distributed across the party — two rules stacked on each other.
/// <para><b>The Gen 1 participation split:</b> every creature that took the field this battle and is still
/// standing splits the award evenly. A creature that fought and was switched back out earns exactly what the
/// finisher earns; a fainted participant earns nothing and is excluded from the divisor (so a forced
/// faint-switch leaves the survivor taking the whole award).</para>
/// <para><b>The innate party Exp-Share</b> (roguelite Exp-All, <see cref="RunRules.BenchXpShare"/>): a living
/// member that <em>never</em> took the field earns a configurable fraction of the <em>full</em> award plus the
/// full Stat-Exp, so a drafted roster keeps pace and stays swappable. A deliberate roguelite deviation living
/// in <see cref="RunRules"/>, not the Gen-1 seam; it never fires for a party-less <see cref="Battle"/>.</para>
/// <para>Because the two are based on different figures, a non-participant can out-earn a participant at the
/// Easy/Normal presets — a known, user-accepted limitation, pinned by a test below.</para>
/// <para>XP is silent for a never-deployed member until it produces a level-up, which surfaces attributed
/// (<c>LeveledUp.OnBench</c>); a switched-out participant's award is logged (<c>ExperienceGained.OnBench</c>)
/// without moving the on-field creature's XP bar.</para>
/// </summary>
public class PartyExpShareTests
{
    // A one-shot lead that wins cleanly, and a low-XP foe with known base stats (for the Stat-Exp assertions).
    private static Creature OneShotLead(string name = "Lead", int level = 60)
    {
        var c = TestCreatures.Make(name, level: level, hp: 400);
        c.Attributes.Attack = 999;
        c.Attributes.Speed = 300;
        c.AddAttack(
            new Attack("Slam", "")
            {
                Id = 1,
                BaseDamage = 250,
                Accuracy = 100,
            }
        );
        return c;
    }

    private static Creature Foe(int hp = 20)
    {
        var enemy = TestCreatures.Make("Foe", level: 30, hp: hp);
        enemy.BaseHP = 10;
        enemy.BaseAttack = 20;
        enemy.BaseDefense = 30;
        enemy.BaseSpecial = 40;
        enemy.BaseSpeed = 50;
        enemy.SpeciesBaseExperience = 200;
        enemy.AddAttack(
            new Attack("Poke", "")
            {
                Id = 2,
                BaseDamage = 1,
                Accuracy = 100,
            }
        );
        return enemy;
    }

    private static Creature Bench(string name, int level = 55)
    {
        var c = TestCreatures.Make(name, level: level, hp: 150);
        c.GrowthRate = GrowthRate.MediumFast;
        c.Experience = c.CalculateExperienceForLevel(level); // sit exactly at the level floor
        return c;
    }

    // The core split: the lead earns the full award (as today), each LIVING bench member earns
    // floor(award × BenchXpShare) plus the foe's full Stat-Exp; a FAINTED bench member earns nothing.
    [Fact]
    public async Task LivingBenchEarnsAShareOfTheAward_FaintedEarnsNothing()
    {
        var lead = OneShotLead();
        var alive = Bench("Alive");
        var fainted = Bench("Fainted");
        fainted.Attributes.HP = 0; // knocked out before the win → excluded from the share

        var party = new Party(lead);
        party.Add(alive);
        party.Add(fainted);

        int leadXpBefore = lead.Experience;
        int aliveXpBefore = alive.Experience;
        int aliveExpHpBefore = alive.ExpHP;

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerUses("Slam")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.5 })
            .Seed(1)
            .RunAsync();

        Assert.Equal("Lead", result.Winner);

        // Exactly one ExperienceGained — the lead's. Bench XP is silent (no per-member log line).
        var xpEvents = result.All<ExperienceGained>();
        Assert.Single(xpEvents);
        Assert.Equal("Lead", xpEvents[0].CreatureName);
        int award = xpEvents[0].Amount;
        Assert.True(award > 0);

        // Lead: full award. Living bench: floor(award × 0.5). Fainted bench: nothing.
        Assert.Equal(award, lead.Experience - leadXpBefore);
        Assert.Equal((int)Math.Floor(award * 0.5), alive.Experience - aliveXpBefore);
        Assert.Equal(fainted.CalculateExperienceForLevel(55), fainted.Experience); // untouched

        // Stat-Exp is shared in full to each living member (the foe's base HP here); the fainted one gets none.
        Assert.Equal(aliveExpHpBefore + 10, alive.ExpHP);
        Assert.Equal(0, fainted.ExpHP);
    }

    // A zero share (the property default / an off run) leaves the bench completely untouched — no XP, no
    // Stat-Exp — so a party-aware battle with the share off matches the legacy "only the active earns" behaviour.
    [Fact]
    public async Task BenchShareOfZero_LeavesBenchUntouched()
    {
        var lead = OneShotLead();
        var bench = Bench("Bench");
        var party = new Party(lead);
        party.Add(bench);

        int benchXpBefore = bench.Experience;

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerUses("Slam")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.0 })
            .Seed(1)
            .RunAsync();

        Assert.Equal("Lead", result.Winner);
        Assert.Equal(benchXpBefore, bench.Experience); // no XP
        Assert.Equal(0, bench.ExpHP); // no Stat-Exp
    }

    // A bench member the share pushes over a level threshold levels up like any other creature, and the event is
    // ATTRIBUTED to it (its name) and flagged OnBench — so the client shows a named panel without touching the
    // on-field creature's nameplate. The high-level lead does not level here, so every LeveledUp is the bench's.
    [Fact]
    public async Task BenchLevelUpIsAttributedAndFlaggedOnBench()
    {
        var lead = OneShotLead(level: 80); // high enough that the full award never crosses a threshold
        var rookie = Bench("Rookie", level: 5); // low floor → the shared XP levels it several times
        var party = new Party(lead);
        party.Add(rookie);

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerUses("Slam")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.5 })
            .Seed(1)
            .RunAsync();

        Assert.Equal("Lead", result.Winner);

        var levelUps = result.All<LeveledUp>();
        Assert.NotEmpty(levelUps);
        Assert.All(
            levelUps,
            e =>
            {
                Assert.Equal("Rookie", e.CreatureName);
                Assert.True(e.OnBench, "a bench Exp-Share level-up must be flagged OnBench");
            }
        );
        Assert.True(rookie.Level > 5, "the shared XP should have levelled the bench rookie");

        // The party strip is fed only by PartyUpdated snapshots — a bench level-up must push a fresh one so the
        // roster panel doesn't read stale until an unrelated later event.
        var snapshots = result.All<PartyUpdated>();
        Assert.NotEmpty(snapshots);
        var rookieRow = snapshots[^1].Members.Single(m => m.Name == "Rookie");
        Assert.Equal(rookie.Level, rookieRow.Level);
        Assert.True(rookieRow.Level > 5);
    }

    // The ON-FIELD creature's own strip row is fed by the same snapshots — its nameplate/HUD follow LeveledUp
    // directly, its strip row does not. So a win where ONLY the active creature levels must still push a
    // PartyUpdated, or the strip shows a stale level right next to a correct nameplate.
    [Fact]
    public async Task ActiveCreatureLevellingAlone_StillPushesAPartySnapshot()
    {
        var lead = OneShotLead(level: 5); // low floor → the award crosses a threshold
        lead.GrowthRate = GrowthRate.MediumFast;
        lead.Experience = lead.CalculateExperienceForLevel(5);
        var bench = Bench("Bench", level: 80); // high, and the share is off → nothing off-field levels
        var party = new Party(lead);
        party.Add(bench);

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerUses("Slam")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.0 })
            .Seed(1)
            .RunAsync();

        Assert.Equal("Lead", result.Winner);

        // Only the on-field creature levelled…
        var levelUps = result.All<LeveledUp>();
        Assert.NotEmpty(levelUps);
        Assert.All(
            levelUps,
            e =>
            {
                Assert.Equal("Lead", e.CreatureName);
                Assert.False(e.OnBench, "the only level-up here is the active creature's");
            }
        );
        Assert.True(lead.Level > 5, "the win should have levelled the on-field creature");
        Assert.Equal(80, bench.Level); // untouched

        // …and the roster panel is still refreshed, with the lead's row carrying the NEW level.
        var snapshots = result.All<PartyUpdated>();
        Assert.NotEmpty(snapshots);
        var leadRow = snapshots[^1].Members.Single(m => m.Name == "Lead");
        Assert.Equal(lead.Level, leadRow.Level);
        Assert.True(leadRow.Level > 5);
    }

    // The bench share is taken off the active's ALREADY-curve-scaled award (RunRules.XpMultiplier*), not the raw
    // Gen-1 base — the production config runs both dials at once (live: 1.5→4.5 curve × 0.5 share). Pin that the
    // bench earns floor(scaledAward × share), i.e. the multiplier compounds into the share as intended.
    [Fact]
    public async Task BenchShareIsTakenOffTheCurveScaledAward()
    {
        var lead = OneShotLead();
        var bench = Bench("Bench");
        var party = new Party(lead);
        party.Add(bench);

        int benchXpBefore = bench.Experience;

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerUses("Slam")
            .EnemyUses("Poke")
            // Flat 2.0 multiplier at every level so the expected award is exact: baseXp × 2.
            .RunRules(
                new RunRules
                {
                    XpMultiplierEarly = 2.0,
                    XpMultiplierLate = 2.0,
                    BenchXpShare = 0.5,
                }
            )
            .Seed(1)
            .RunAsync();

        Assert.Equal("Lead", result.Winner);

        int baseXp = Gen1BattleRules.Instance.CalculateXpAwarded(200, 30, trainerOwned: false);
        int award = result.All<ExperienceGained>().Single().Amount;
        Assert.Equal(baseXp * 2, award); // the run XP curve applied to the lead's award
        // …and the bench share is floor(that scaled award × 0.5), not floor(baseXp × 0.5).
        Assert.Equal((int)Math.Floor(award * 0.5), bench.Experience - benchXpBefore);
    }

    // ---- Participation XP (2026-07-27): every creature that took the field splits the award evenly ----

    // A hard hitter that can win a fight on its own, sitting exactly at its level floor so an award's XP delta
    // is exact and no level-up fires. Used on BOTH sides of a voluntary switch, so a battle can end with two
    // LIVE participants — the case the split is about.
    private static Creature Striker(string name, string moveName, int level = 60)
    {
        var c = TestCreatures.Make(name, level: level, hp: 400);
        c.Attributes.Attack = 999;
        c.Attributes.Speed = 300;
        c.GrowthRate = GrowthRate.MediumFast;
        c.Experience = c.CalculateExperienceForLevel(level);
        c.AddAttack(
            new Attack(moveName, "")
            {
                Id = 1,
                BaseDamage = 250,
                Accuracy = 100,
            }
        );
        return c;
    }

    /// <summary>The undivided Gen 1 award for beating <see cref="Foe"/> (base exp 200, level 30, wild).</summary>
    private static int FullAward() =>
        Gen1BattleRules.Instance.CalculateXpAwarded(200, 30, trainerOwned: false);

    // THE HEADLINE REQUIREMENT: taking the field is what makes you a participant. A creature that fought and was
    // switched back out earns exactly what the creature that happened to land the last hit earns — participants
    // are not ranked by who was standing there when the enemy fainted.
    [Fact]
    public async Task SwitchedOutParticipantEarnsTheSameShareAsTheFinisher()
    {
        var lead = Striker("Lead", "Slam");
        var sub = Striker("Sub", "Bash");
        var rester = Bench("Rester");

        var party = new Party(lead);
        party.Add(sub);
        party.Add(rester);

        int leadBefore = lead.Experience;
        int subBefore = sub.Experience;
        int resterBefore = rester.Experience;

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerTurnPlan(1) // turn 1: SWITCH to Sub — Lead fought, then left the field
            .PlayerUses("Bash") // turn 2: Sub finishes the foe
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.5 })
            .Seed(1)
            .RunAsync();

        Assert.Equal("Sub", result.Winner);

        int fullAward = FullAward();
        int share = fullAward / 2; // two live participants, evenly split (Gen 1)

        Assert.Equal(share, sub.Experience - subBefore);
        Assert.Equal(share, lead.Experience - leadBefore);
        // …and the finisher is no longer paid the WHOLE award just for being the one left standing.
        Assert.NotEqual(fullAward, sub.Experience - subBefore);

        // A member that never took the field is not a participant — it earns the innate bench share instead.
        Assert.Equal((int)Math.Floor(fullAward * 0.5), rester.Experience - resterBefore);

        // Exactly one award event per participant (nobody is paid twice — the easy double-pay bug here, since
        // the active creature appears both at the award site and in the participant set). The finisher's drives
        // the on-field XP bar; the switched-out participant's is flagged so the client logs it WITHOUT moving
        // that bar. A never-deployed member stays silent, as before.
        var xpEvents = result.All<ExperienceGained>();
        Assert.Equal(2, xpEvents.Count);
        Assert.Equal(new[] { "Sub", "Lead" }, xpEvents.Select(e => e.CreatureName).ToArray());
        Assert.False(xpEvents[0].OnBench);
        Assert.True(xpEvents[1].OnBench);
        Assert.All(xpEvents, e => Assert.Equal(share, e.Amount));
    }

    // A switched-out participant is paid through a different path than the innate bench share, so its level-up
    // surfacing needs its own guard: the LeveledUp must be attributed to it and flagged off-field, so the client
    // shows that creature's stat panel without dragging the on-field creature's nameplate/XP bar with it.
    [Fact]
    public async Task SwitchedOutParticipantLevelsUpAttributedAndFlaggedOffField()
    {
        var rookie = Striker("Rookie", "Slam", level: 5); // its half-share crosses several thresholds
        var veteran = Striker("Vet", "Bash", level: 80); // finishes; too high for the share to level it

        var party = new Party(rookie);
        party.Add(veteran);

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerTurnPlan(1) // turn 1: SWITCH — Rookie fought, then left the field
            .PlayerUses("Bash") // turn 2: Vet finishes
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.5 })
            .Seed(1)
            .RunAsync();

        Assert.Equal("Vet", result.Winner);
        Assert.True(
            rookie.Level > 5,
            "the switched-out participant's split share should have levelled it"
        );

        var levelUps = result.All<LeveledUp>();
        Assert.NotEmpty(levelUps);
        Assert.All(
            levelUps,
            e =>
            {
                Assert.Equal("Rookie", e.CreatureName);
                Assert.True(
                    e.OnBench,
                    "a switched-out participant's level-up must be flagged off-field"
                );
            }
        );

        // …and the roster panel is refreshed, since it is fed only by PartyUpdated snapshots.
        var snapshot = result.All<PartyUpdated>()[^1];
        Assert.Equal(rookie.Level, snapshot.Members.Single(m => m.Name == "Rookie").Level);
    }

    // KNOWN, DELIBERATELY ACCEPTED LIMITATION (user's call, 2026-07-27): participants split the award while the
    // innate bench share is still taken off the FULL award, so a creature that never fought can out-earn one
    // that did — equal at Normal (0.5), strictly more at Easy (0.75). Pinned here so the inversion is a decision
    // on the record rather than a regression someone "fixes" by accident. See docs/TODO.md → Participation XP.
    [Fact]
    public async Task BenchShareIsTakenOffTheFullAward_SoANonParticipantCanOutEarnAParticipant()
    {
        var lead = Striker("Lead", "Slam");
        var sub = Striker("Sub", "Bash");
        var rester = Bench("Rester");

        var party = new Party(lead);
        party.Add(sub);
        party.Add(rester);

        int subBefore = sub.Experience;
        int resterBefore = rester.Experience;

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerTurnPlan(1)
            .PlayerUses("Bash")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.75 }) // the Easy preset
            .Seed(1)
            .RunAsync();

        Assert.Equal("Sub", result.Winner);

        int fullAward = FullAward();
        int participantShare = sub.Experience - subBefore;
        int benchShare = rester.Experience - resterBefore;

        Assert.Equal(fullAward / 2, participantShare);
        Assert.Equal((int)Math.Floor(fullAward * 0.75), benchShare);
        Assert.True(
            benchShare > participantShare,
            "accepted limitation: at Easy the bench share (off the full award) exceeds a participant's split share"
        );
    }

    // A fainted participant earns nothing AND is excluded from the divisor (Gen 1). So the forced faint-switch
    // is never penalised by the split: the survivor is the only LIVE participant and takes the whole award —
    // identical to the behaviour before participation was tracked at all.
    [Fact]
    public async Task FaintedParticipantEarnsNothingAndIsExcludedFromTheDivisor()
    {
        var frail = TestCreatures.Make("Frail", level: 60, hp: 1);
        frail.Attributes.Speed = 300; // acts first, so its scripted move is consumed before the KO
        frail.GrowthRate = GrowthRate.MediumFast;
        frail.Experience = frail.CalculateExperienceForLevel(60);
        frail.AddAttack(
            new Attack("Tap", "")
            {
                Id = 3,
                BaseDamage = 1,
                Accuracy = 100,
            }
        );

        var striker = Striker("Striker", "Slam");
        var party = new Party(frail);
        party.Add(striker);

        int frailBefore = frail.Experience;
        int strikerBefore = striker.Experience;

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            // Turn 1: Frail taps, the foe KOs it → forced switch. Turn 2: Striker finishes.
            .PlayerUses("Tap", "Slam")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.5 })
            .Seed(1)
            .RunAsync();

        Assert.Equal("Striker", result.Winner);
        Assert.False(frail.IsAlive());

        int fullAward = FullAward();
        Assert.Equal(fullAward, striker.Experience - strikerBefore); // sole LIVE participant ⇒ undivided
        Assert.Equal(frailBefore, frail.Experience); // fainted ⇒ nothing
        Assert.Single(result.All<ExperienceGained>());
    }

    // MUTUAL KO: the enemy-faint check runs BEFORE the player-faint branch, so a finisher that dies on the same
    // turn it wins (here end-of-turn Burn; Self-Destruct and Struggle recoil reach the same place) arrives at the
    // award site already fainted. It is a fainted participant like any other — it earns nothing and is not
    // counted in the divisor, so the creature that fought and survived takes the award UNDIVIDED. Without the
    // IsAlive() guards this paid the corpse a half share and told the client to fill its XP bar.
    [Fact]
    public async Task MutualKo_FaintedFinisherEarnsNothingAndIsExcludedFromTheDivisor()
    {
        var survivor = Striker("Survivor", "Slam"); // fights turn 1, switches out, still standing at the end
        var doomed = Striker("Doomed", "Bash");
        doomed.Attributes.HP = 30; // Burn chips maxHP/16 = 25 per turn → dead at the end of turn 2
        doomed.CarriedStatus = new CarriedStatus(StatusCondition.Burn, 0);

        var party = new Party(survivor);
        party.Add(doomed);

        int survivorBefore = survivor.Experience;
        int doomedBefore = doomed.Experience;

        var result = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerTurnPlan(1) // turn 1: SWITCH — Survivor fought, Doomed comes in burned
            .PlayerUses("Bash") // turn 2: Doomed wins the fight and burns to death the same turn
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.5 })
            .Seed(1)
            .RunAsync();

        Assert.False(doomed.IsAlive(), "the finisher must have fainted on the winning turn");
        Assert.True(survivor.IsAlive());

        int fullAward = FullAward();
        Assert.Equal(fullAward, survivor.Experience - survivorBefore); // sole LIVE participant ⇒ undivided
        Assert.Equal(doomedBefore, doomed.Experience); // fainted ⇒ nothing, despite landing the KO

        // …and only the survivor's award is announced. The dead finisher must not emit an on-field
        // ExperienceGained, which would fill the XP bar of a creature that just fainted.
        var xpEvents = result.All<ExperienceGained>();
        Assert.Equal("Survivor", Assert.Single(xpEvents).CreatureName);
        Assert.True(
            xpEvents[0].OnBench,
            "the survivor is off-field here, so its award must not drive the bar"
        );
    }

    // The participant SET gates the split, not the bench share: with the share switched off entirely, a creature
    // that fought is still paid its equal share (only the never-deployed members lose out).
    [Fact]
    public async Task BenchShareOfZero_StillPaysEveryParticipantItsSplitShare()
    {
        var lead = Striker("Lead", "Slam");
        var sub = Striker("Sub", "Bash");
        var rester = Bench("Rester");

        var party = new Party(lead);
        party.Add(sub);
        party.Add(rester);

        int leadBefore = lead.Experience;
        int subBefore = sub.Experience;
        int resterBefore = rester.Experience;

        await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerTurnPlan(1)
            .PlayerUses("Bash")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.0 })
            .Seed(1)
            .RunAsync();

        int share = FullAward() / 2;
        Assert.Equal(share, lead.Experience - leadBefore);
        Assert.Equal(share, sub.Experience - subBefore);
        Assert.Equal(resterBefore, rester.Experience); // never took the field, share off ⇒ nothing
    }

    // A party-less Battle has exactly one participant, so the split is a 1-way division = the full award. Pins
    // that every direct single-creature caller (tests, the legacy endless chain) is untouched by this change.
    [Fact]
    public async Task NoPartyWired_TheSoleParticipantStillEarnsTheWholeAward()
    {
        var solo = Striker("Solo", "Slam");
        int before = solo.Experience;

        var result = await new BattleScenario()
            .Player(solo)
            .Enemy(Foe())
            .PlayerUses("Slam")
            .EnemyUses("Poke")
            .Seed(1)
            .RunAsync();

        Assert.Equal("Solo", result.Winner);
        Assert.Equal(FullAward(), solo.Experience - before);
        Assert.Equal(FullAward(), result.All<ExperienceGained>().Single().Amount);
    }

    // Participation is per-BATTLE. This is the regression guard for the trap that ruled out tracking it as a
    // flag on Creature.BattleState: ResetBattleState() only ever reaches the active creature and the enemy, so a
    // benched creature is NEVER reset between battles and a per-creature flag set in battle 1 would still read
    // true in battle 2 — silently paying a participant's share to a creature that never took the field.
    [Fact]
    public async Task ParticipationDoesNotLeakIntoTheNextBattle()
    {
        var lead = Striker("Lead", "Slam");
        var sub = Striker("Sub", "Bash");
        var party = new Party(lead);
        party.Add(sub);

        // Battle 1: Lead fights, switches out, Sub finishes → both participants.
        await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerTurnPlan(1)
            .PlayerUses("Bash")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.25 })
            .Seed(1)
            .RunAsync();

        Assert.Same(sub, party.Lead); // the switch left Sub leading, so it leads battle 2

        int leadBefore = lead.Experience;
        int subBefore = sub.Experience;

        // Battle 2: a FRESH Battle. Sub fights alone; Lead never takes the field.
        var second = await new BattleScenario()
            .Party(party)
            .Enemy(Foe())
            .PlayerUses("Bash")
            .EnemyUses("Poke")
            .RunRules(new RunRules { BenchXpShare = 0.25 })
            .Seed(1)
            .RunAsync();

        Assert.Equal("Sub", second.Winner);

        int fullAward = FullAward();
        Assert.Equal(fullAward, sub.Experience - subBefore); // sole live participant ⇒ undivided
        Assert.Equal((int)Math.Floor(fullAward * 0.25), lead.Experience - leadBefore); // bench, not participant
        // The two figures are deliberately distinct (0.25 share ≠ a half split), so a leak can't pass unnoticed.
        Assert.NotEqual(fullAward / 2, lead.Experience - leadBefore);
    }
}
