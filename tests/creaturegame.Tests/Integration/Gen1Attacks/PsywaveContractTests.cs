using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Psywave (Gen 1) deals a random 1..floor(1.5 × user level), ignoring Attack/Defense, type
/// effectiveness, STAB and crits. These tests exercise the gen-variable <i>quirk</i> — the bound is
/// set only by the user's level, and neither attacker stats nor defender bulk move it — rather than
/// just the import mapping (pinned in <see cref="SecondaryChanceDataContractTests"/>). There is no
/// type-immunity case to cover: Psywave is Psychic, and no Gen 1 type is immune to Psychic.
/// </summary>
[Collection(MovesCollection.Name)]
public class PsywaveContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData(50)] // cap = 75
    [InlineData(30)] // cap = 45
    [InlineData(100)] // cap = 150
    public async Task DealsVariableDamageBoundedByOnePointFiveLevel(int level)
    {
        int cap = level * 3 / 2;
        var observed = new HashSet<int>();

        // Sample many RNG seeds: each run is one real AttackAction through the Gen 1 Psywave path.
        for (int seed = 0; seed < 200; seed++)
        {
            var result = await new MoveScenario()
                .Rng(new SeededRandomSource(seed))
                .Attacker(TestCreatures.Make("A", level: level, special: 100))
                .Defender(TestCreatures.Make("D", hp: 9999, defense: 100, special: 100))
                .Use(Move("psywave"));

            Assert.True(result.Has<DamageDealt>(), "Psywave deals damage on hit");
            int dmg = result.TotalDamage;
            Assert.InRange(dmg, 1, cap); // Gen 1: 1..floor(1.5 × level), inclusive
            observed.Add(dmg);
        }

        // It must actually be variable (not a fixed value) and span toward the level-derived cap —
        // proof the magnitude is bounded by level, not collapsed to a constant or a stat-driven number.
        Assert.True(observed.Count > 1, "Psywave damage should vary across rolls");
        Assert.True(
            observed.Max() > cap * 2 / 3,
            $"rolls should reach the upper range (max {observed.Max()} of cap {cap})"
        );
    }

    // The quirk that distinguishes Psywave from a Standard Special move: the damage ignores the
    // attacker's Special and the defender's bulk/type entirely. For a fixed level + RNG seed, a glass
    // attacker into a wall defender must produce exactly the same damage as a strong attacker into a
    // paper defender — only level and the roll matter.
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(42)]
    public async Task IgnoresAttackerSpecialAndDefenderBulk(int seed)
    {
        async Task<int> Roll(int attackerSpecial, DamageType defenderType, int defenderBulk)
        {
            var result = await new MoveScenario()
                .Rng(new SeededRandomSource(seed))
                .Attacker(TestCreatures.Make("A", level: 50, special: attackerSpecial))
                .Defender(
                    TestCreatures.Make(
                        "D",
                        type1: defenderType,
                        hp: 9999,
                        defense: defenderBulk,
                        special: defenderBulk
                    )
                )
                .Use(Move("psywave"));
            return result.TotalDamage;
        }

        int weakIntoWall = await Roll(attackerSpecial: 1, DamageType.Psychic, defenderBulk: 250);
        int strongIntoPaper = await Roll(attackerSpecial: 255, DamageType.Water, defenderBulk: 1);

        Assert.Equal(weakIntoWall, strongIntoPaper);
    }
}
