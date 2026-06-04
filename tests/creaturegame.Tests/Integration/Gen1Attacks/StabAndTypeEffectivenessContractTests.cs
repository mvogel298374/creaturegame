using creaturegame.Attacks;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Damage-scaling contracts driven by the type system: same-type attack bonus (~1.5×) and the
/// Gen 1 type chart multiplying or reducing damage. Both compare a real move's damage between two
/// otherwise-identical setups, with variance and crits switched off so the ratio is pure.
/// </summary>
[Collection(MovesCollection.Name)]
public class StabAndTypeEffectivenessContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("pound",       DamageType.Normal,   DamageType.Water,  DamageType.Fire)]
    [InlineData("fire-punch",  DamageType.Fire,     DamageType.Normal, DamageType.Electric)]
    [InlineData("karate-chop", DamageType.Normal,   DamageType.Water,  DamageType.Fire)]   // Normal in Gen 1 (was Fighting)
    [InlineData("wing-attack", DamageType.Flying,   DamageType.Normal, DamageType.Normal)]
    [InlineData("vine-whip",   DamageType.Grass,    DamageType.Normal, DamageType.Normal)]   // first Special-type STAB mover
    [InlineData("jump-kick",   DamageType.Fighting, DamageType.Normal, DamageType.Fire)]
    [InlineData("poison-sting", DamageType.Poison,  DamageType.Normal, DamageType.Normal)]
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

    [Theory]
    [InlineData("fire-punch",    DamageType.Grass,    DamageType.Normal, 2.0)]
    [InlineData("fire-punch",    DamageType.Water,    DamageType.Normal, 0.5)]
    [InlineData("karate-chop",   DamageType.Rock,     DamageType.Normal, 0.5)]   // Normal in Gen 1 → resisted by Rock (was Fighting → Normal 2×)
    [InlineData("thunder-punch", DamageType.Water,    DamageType.Normal, 2.0)]
    [InlineData("wing-attack",   DamageType.Grass,    DamageType.Normal, 2.0)]   // Flying super-effectiveness coverage (gust was retyped to Normal in Gen 1)
    [InlineData("vine-whip",     DamageType.Water,    DamageType.Normal, 2.0)]   // Grass → Water
    [InlineData("jump-kick",     DamageType.Normal,   DamageType.Fire,   2.0)]   // Fighting → Normal
    [InlineData("poison-sting",  DamageType.Grass,    DamageType.Normal, 2.0)]   // Poison → Grass
    public async Task TypeEffectivenessScalesDamage(
        string moveName, DamageType defenderType, DamageType neutralDefenderType, double expectedMult)
    {
        var move = Move(moveName);

        async Task<int> Damage(DamageType type)
        {
            var result = await new MoveScenario()
                .Attacker(TestCreatures.Make("A", attack: 250, special: 250))
                .Defender(TestCreatures.Make("D", type1: type, hp: 9999, defense: 40, special: 40))
                .Rules(NoVarianceNoCritHitRules.Instance)
                .Use(move);
            return result.TotalDamage;
        }

        double ratio = (double)await Damage(defenderType) / await Damage(neutralDefenderType);
        Assert.InRange(ratio, expectedMult * 0.8, expectedMult * 1.2);
    }
}
