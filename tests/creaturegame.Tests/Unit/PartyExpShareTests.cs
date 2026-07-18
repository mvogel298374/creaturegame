using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Innate party Exp-Share (roguelite Exp-All, <see cref="RunRules.BenchXpShare"/>): after a win the active
/// creature is paid in full (unchanged Gen-1 pace), then every LIVING bench member earns a configurable
/// fraction of that award plus the full Stat-Exp — so a drafted roster keeps pace and stays swappable. A
/// fainted member earns nothing. Bench XP is silent until it produces a level-up, which surfaces attributed
/// (<c>LeveledUp.OnBench</c>). A deliberate roguelite deviation from Gen 1's participant split — the share lives
/// in <see cref="RunRules"/>, not the Gen-1 seam, and never fires for a party-less <see cref="Battle"/>.
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

    private static Creature Foe()
    {
        var enemy = TestCreatures.Make("Foe", level: 30, hp: 20);
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
}
