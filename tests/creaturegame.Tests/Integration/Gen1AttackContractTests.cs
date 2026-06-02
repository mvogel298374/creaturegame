using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Behaviour coverage for Gen 1 attacks — batch 1 (move IDs 1–10). Each "contract" is a
/// parametrized <see cref="TheoryAttribute"/> run over every move that has that effect, so a
/// shared effect (e.g. "deals damage") is asserted once and reused across moves (the xUnit
/// equivalent of NUnit <c>[TestCase]</c>). Moves are the real rows from <c>moves.db</c>
/// (<see cref="MovesFixture"/>) given to a creature via <see cref="MoveScenario"/>.
///
/// Batch 1 moves: pound, karate-chop, double-slap, comet-punch, mega-punch, pay-day,
/// fire-punch, ice-punch, thunder-punch, scratch.
/// </summary>
[Collection(MovesCollection.Name)]
public class Gen1AttackContractTests(MovesFixture moves)
{
    private Attack Move(string name) => moves.Get(name);

    // ── Damage + PP (every damaging move) ────────────────────────────────────

    [Theory]
    [InlineData("pound")] [InlineData("karate-chop")] [InlineData("double-slap")]
    [InlineData("comet-punch")] [InlineData("mega-punch")] [InlineData("pay-day")]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    [InlineData("scratch")]
    public async Task DealsDamageOnHit(string moveName)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("Defender", hp: 500, defense: 80, special: 80))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>(), "expected a DamageDealt event");
        Assert.True(result.TotalDamage > 0, "expected non-zero damage");
        Assert.True(result.Defender.Attributes.HP < result.Defender.Attributes.MaxHP);
    }

    [Theory]
    [InlineData("pound")] [InlineData("karate-chop")] [InlineData("double-slap")]
    [InlineData("comet-punch")] [InlineData("mega-punch")] [InlineData("pay-day")]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    [InlineData("scratch")]
    public async Task DecrementsPpByOneOnUse(string moveName)
    {
        var move = Move(moveName);
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("Defender", hp: 9999, defense: 250))
            .Use(move);

        Assert.Equal(move.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }

    // ── Accuracy (the 85%-accuracy movers can miss) ──────────────────────────

    [Theory]
    [InlineData("double-slap")] [InlineData("comet-punch")] [InlineData("mega-punch")]
    public async Task MissesWhenAccuracyRollFails(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(result.Defender.Attributes.MaxHP, result.Defender.Attributes.HP);
    }

    // ── Secondary status (the elemental punches) ─────────────────────────────

    [Theory]
    [InlineData("fire-punch", StatusCondition.Burn)]
    [InlineData("ice-punch", StatusCondition.Freeze)]
    [InlineData("thunder-punch", StatusCondition.Paralysis)]
    public async Task AppliesSecondaryStatusOnHit(string moveName, StatusCondition expected)
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.Equal(expected, result.Defender.Status);
        Assert.Contains(result.Events, e => e is StatusApplied);
    }

    [Theory]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    public async Task NoSecondaryStatusOnMiss(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Use(Move(moveName));

        Assert.Equal(StatusCondition.None, result.Defender.Status);
    }

    [Theory]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    public async Task NoSecondaryStatusWhenTargetAlreadyStatused(string moveName)
    {
        var defender = TestCreatures.Make("Defender", hp: 500);
        defender.Status = StatusCondition.Poison;

        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(defender)
            .Use(Move(moveName));

        Assert.Equal(StatusCondition.Poison, result.Defender.Status);   // not overwritten
    }

    // ── Gen 1 physical/special-by-type: the punches are SPECIAL ──────────────

    [Theory]
    [InlineData("fire-punch")] [InlineData("ice-punch")] [InlineData("thunder-punch")]
    public void ElementalPunchesAreSpecialInGen1(string moveName)
        => Assert.Equal(AttackType.Special, Move(moveName).AttackType);

    // ── High-crit (Karate Chop) ──────────────────────────────────────────────

    [Fact]
    public async Task HighCritMoveCritsFarMoreOftenThanNormal()
    {
        async Task<int> CritRuns(string moveName)
        {
            int crits = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                var result = await new MoveScenario()
                    .Attacker(TestCreatures.Make("A", baseSpeed: 100))
                    .Defender(TestCreatures.Make("D", hp: 9999, defense: 250))
                    .Rng(new SeededRandomSource(seed))
                    .Use(Move(moveName));
                if (result.Hits.Any(h => h.IsCrit)) crits++;
            }
            return crits;
        }

        int karateChop = await CritRuns("karate-chop");   // high-crit ⇒ crits almost always
        int pound      = await CritRuns("pound");          // normal crit rate

        Assert.True(karateChop > pound, $"karate-chop crits ({karateChop}) should exceed pound ({pound})");
        Assert.True(karateChop >= 40, $"high-crit move should crit most of the time (got {karateChop}/60)");
    }

    // ── STAB (same-type ⇒ ~1.5×) ─────────────────────────────────────────────

    [Theory]
    [InlineData("pound",       DamageType.Normal,   DamageType.Water,  DamageType.Fire)]
    [InlineData("fire-punch",  DamageType.Fire,     DamageType.Normal, DamageType.Electric)]
    [InlineData("karate-chop", DamageType.Fighting, DamageType.Normal, DamageType.Water)]
    public async Task StabAddsAboutHalfAgainDamage(
        string moveName, DamageType stabType, DamageType neutralAttackerType, DamageType defenderType)
    {
        var move = Move(moveName);

        async Task<int> Damage(DamageType attackerType)
        {
            var result = await new MoveScenario()
                .Attacker(TestCreatures.Make("A", type1: attackerType, attack: 200, special: 200))
                .Defender(TestCreatures.Make("D", type1: defenderType, hp: 9999, defense: 60, special: 60))
                .Rules(NoVarianceNoCritHitRules.Instance)
                .Use(move);
            return result.TotalDamage;
        }

        double ratio = (double)await Damage(stabType) / await Damage(neutralAttackerType);
        Assert.InRange(ratio, 1.35, 1.65);
    }

    // ── Type effectiveness scales damage ─────────────────────────────────────

    [Theory]
    [InlineData("fire-punch",    DamageType.Grass,  DamageType.Normal, 2.0)]
    [InlineData("fire-punch",    DamageType.Water,  DamageType.Normal, 0.5)]
    [InlineData("karate-chop",   DamageType.Normal, DamageType.Water,  2.0)]
    [InlineData("thunder-punch", DamageType.Water,  DamageType.Normal, 2.0)]
    public async Task TypeEffectivenessScalesDamage(
        string moveName, DamageType defenderType, DamageType neutralDefenderType, double expectedMult)
    {
        var move = Move(moveName);

        async Task<int> Damage(DamageType defenderType)
        {
            var result = await new MoveScenario()
                .Attacker(TestCreatures.Make("A", attack: 250, special: 250))
                .Defender(TestCreatures.Make("D", type1: defenderType, hp: 9999, defense: 40, special: 40))
                .Rules(NoVarianceNoCritHitRules.Instance)
                .Use(move);
            return result.TotalDamage;
        }

        double ratio = (double)await Damage(defenderType) / await Damage(neutralDefenderType);
        Assert.InRange(ratio, expectedMult * 0.8, expectedMult * 1.2);
    }

    // ── Multi-hit (Double Slap, Comet Punch) ─────────────────────────────────

    [Theory]
    [InlineData("double-slap")] [InlineData("comet-punch")]
    public async Task MultiHitStrikesTwoToFiveTimes(string moveName)
    {
        for (int seed = 0; seed < 25; seed++)
        {
            var result = await new MoveScenario()
                .Defender(TestCreatures.Make("D", hp: 9999, defense: 250))
                .Rng(new SeededRandomSource(seed))
                .Use(Move(moveName));

            int hits = result.Hits.Count;
            Assert.InRange(hits, 2, 5);
            var summary = result.First<MultiHitCompleted>();
            Assert.NotNull(summary);
            Assert.Equal(hits, summary!.Hits);
        }
    }

    [Theory]
    [InlineData("double-slap")] [InlineData("comet-punch")]
    public async Task MultiHitWithFixedCountStrikesExactlyThatMany(string moveName)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 9999, defense: 250))
            .Rules(new FixedMultiHitRules(3))
            .Use(Move(moveName));

        Assert.Equal(3, result.Hits.Count);
        Assert.Equal(3, result.First<MultiHitCompleted>()!.Hits);
        Assert.Equal(result.TotalDamage, result.Defender.Attributes.MaxHP - result.Defender.Attributes.HP);
    }

    // ── Pay Day (coin scatter) ───────────────────────────────────────────────

    [Fact]
    public async Task PayDayScattersCoinsEqualToMultiplierTimesLevel()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("pay-day"));

        var coins = result.First<CoinsScattered>();
        Assert.NotNull(coins);
        Assert.Equal(Gen1BattleRules.Instance.PayDayCoinMultiplier * 50, coins!.Amount);
        Assert.True(result.Has<DamageDealt>(), "Pay Day also deals damage");
    }

    // ── The Gen 1 multi-hit distribution itself ──────────────────────────────

    [Fact]
    public void RollMultiHitCountStaysInTwoToFiveAndFavoursLowCounts()
    {
        var counts = new Dictionary<int, int> { [2] = 0, [3] = 0, [4] = 0, [5] = 0 };
        for (int seed = 0; seed < 2000; seed++)
        {
            int n = new Gen1BattleRules(new SeededRandomSource(seed)).RollMultiHitCount();
            Assert.InRange(n, 2, 5);
            counts[n]++;
        }
        Assert.True(counts[2] + counts[3] > counts[4] + counts[5], "2–3 hits should dominate 4–5");
    }
}
