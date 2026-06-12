using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Locks down the RNG seam at the one spot it used to leak: a test double's UNPINNED rolls. The base
/// <see cref="DelegatingBattleRules"/> (parent of <see cref="ScriptableRules"/> and the other doubles) used to
/// hardcode the global <c>Gen1BattleRules.Instance</c>, so any roll a seeded <see cref="BattleScenario"/> did
/// not explicitly pin drew from <c>Random.Shared</c> and went test-order-flaky. The seam now lets a double
/// share a seeded source, and these tests prove a seeded double draws from the injected source — never the
/// global RNG — and that a seeded <see cref="BattleScenario"/> replays identically end to end.
///
/// This edge is CLOSED. A seeded battle is fully reproducible; do not re-file it as "the rules ignore the
/// battle seed" or "Roll* draws are nondeterministic."
/// </summary>
public class SeededRulesTests
{
    [Fact]
    public void SeededRules_DrawUnpinnedRollFromInjectedSource_NotGlobalRng()
    {
        // Gen1BattleRules.RollSleepTurns() is _rng.Next(1, 8). A bare probe on the same seed predicts the exact
        // value — only possible if the double pulls from the injected source instead of Random.Shared. This is
        // the very construction BattleScenario now uses: new ScriptableRules(new SeededRandomSource(seed)).
        var probe = new SeededRandomSource(1234);
        int expected = probe.Next(1, 8);

        var rules = new ScriptableRules(new SeededRandomSource(1234));

        Assert.Equal(expected, rules.RollSleepTurns());
    }

    [Fact]
    public void SeededRules_AreReproducible_AcrossManyUnpinnedRolls()
    {
        var a = new ScriptableRules(new SeededRandomSource(99));
        var b = new ScriptableRules(new SeededRandomSource(99));

        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(a.RollSleepTurns(), b.RollSleepTurns());
            Assert.Equal(a.RollMultiHitCount(), b.RollMultiHitCount());
            Assert.Equal(a.RollBindingTurns(), b.RollBindingTurns());
            Assert.Equal(a.RollConfusionTurns(), b.RollConfusionTurns());
        }
    }

    [Fact]
    public void UnseededRules_StillDelegateToGen1_WithoutThrowing()
    {
        // No seed → falls back to the global singleton, exactly as before the seam existed.
        var rules = new ScriptableRules();
        Assert.InRange(rules.RollSleepTurns(), 1, 7);
    }

    [Fact]
    public async Task SeededBattleScenario_ReplaysIdentically_IncludingUnpinnedSleepRoll()
    {
        // A sleep move's duration (RollSleepTurns) is NOT pinned by Deterministic(); before the seam fix it
        // drew from the global RNG, so a seeded run was not actually reproducible. Same seed twice must now
        // produce a byte-identical event stream.
        static async Task<string> RunOnce()
        {
            var result = await new BattleScenario()
                .Player(WithMoves("Hero", speed: 100, maxHp: 220, attack: 120, Spore(), Tackle()))
                .Enemy(WithMoves("Foe", speed: 1, maxHp: 260, attack: 1, Tackle()))
                .PlayerUses("spore", "tackle")
                .EnemyUses("tackle")
                .Seed(7)
                .RunAsync();
            return string.Join("\n", result.Events.Select(e => e.ToString()));
        }

        string first = await RunOnce();
        string second = await RunOnce();

        Assert.Equal(first, second);
    }

    // Distinct Ids: Creature.AddAttack dedupes by Attack.Id, so same-Id moves (the default 0) collapse to one.
    private static Attack Spore() =>
        new()
        {
            Id = 901,
            Name = "spore",
            BaseDamage = 0,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            StatusEffect = StatusCondition.Sleep,
            EffectChance = 100,
        };

    private static Attack Tackle() =>
        new()
        {
            Id = 902,
            Name = "tackle",
            BaseDamage = 40,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
        };

    private static Creature WithMoves(
        string name,
        int speed,
        int maxHp,
        int attack,
        params Attack[] moves
    )
    {
        var c = new Creature(name)
        {
            Level = 50,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(50);
        // Pin every stat damage depends on — CalculateStats rolls random DVs, so leaving Defense/Special to it
        // would make a freshly-built creature's stats differ per run (creature-construction RNG, outside the
        // battle seed). Fixed stats keep the battle's outcome a pure function of the seed.
        c.Attributes.MaxHP = maxHp;
        c.Attributes.HP = maxHp;
        c.Attributes.Attack = attack;
        c.Attributes.Defense = 100;
        c.Attributes.Special = 100;
        c.Attributes.Speed = speed;
        foreach (var m in moves)
            c.AddAttack(m);
        return c;
    }
}
