using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Level-based fixed damage (Seismic Toss / Night Shade): deals damage equal to the user's level,
/// independent of base power, the attacker/defender stats, and the type matchup. (The Gen 1
/// Fighting/Normal → Ghost immunity for these is a documented simplification — not modelled here.)
/// </summary>
[Collection(MovesCollection.Name)]
public class LevelBasedDamageContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData(50)] [InlineData(30)] [InlineData(100)]
    public async Task SeismicTossDealsDamageEqualToTheUsersLevel(int level)
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: level))
            .Defender(TestCreatures.Make("D", hp: 9999, defense: 250, special: 250))
            .Use(Move("seismic-toss"));

        Assert.Equal(level, result.TotalDamage);
    }

    [Theory]
    [InlineData(DamageType.Water)]
    [InlineData(DamageType.Psychic)]
    public async Task SeismicTossIgnoresDefenderBulkAndType(DamageType defenderType)
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: 50))
            .Defender(TestCreatures.Make("D", type1: defenderType, hp: 9999, defense: 1, special: 1))
            .Use(Move("seismic-toss"));

        Assert.Equal(50, result.TotalDamage);
    }

    // Night Shade is the Ghost-type level-based mover; against a non-immune type it too deals exactly
    // the user's level, ignoring bulk and the (non-zero) type matchup. (Ghost → Normal = 0× immunity
    // is covered in ImmunityContractTests.)
    [Theory]
    [InlineData(30)] [InlineData(50)] [InlineData(100)]
    public async Task NightShadeDealsDamageEqualToTheUsersLevel(int level)
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A", level: level))
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 9999, defense: 250, special: 250))
            .Use(Move("night-shade"));

        Assert.Equal(level, result.TotalDamage);
    }
}
