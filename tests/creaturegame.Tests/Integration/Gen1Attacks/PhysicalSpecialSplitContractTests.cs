using creaturegame.Attacks;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// In Gen 1 a move's physical/special category is decided by its <b>type</b>, not by a per-move
/// flag (the Gen 4+ split). Fire/Ice/Electric are Special; Normal/Fighting/Flying are Physical.
/// This guards the importer's category derivation against regressing for the imported rows — the
/// exact bug that once miscategorised 18 of 110 damaging moves (see DATA_IMPORT §4.1).
/// </summary>
[Collection(MovesCollection.Name)]
public class PhysicalSpecialSplitContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    // Special types
    [InlineData("fire-punch",    AttackType.Special)]   // Fire
    [InlineData("ice-punch",     AttackType.Special)]   // Ice
    [InlineData("thunder-punch", AttackType.Special)]   // Electric
    // Physical types
    [InlineData("pound",         AttackType.Physical)]  // Normal
    [InlineData("scratch",       AttackType.Physical)]  // Normal
    [InlineData("cut",           AttackType.Physical)]  // Normal
    [InlineData("vice-grip",     AttackType.Physical)]  // Normal
    [InlineData("karate-chop",   AttackType.Physical)]  // Normal (Gen 1; retyped to Fighting in Gen 2)
    [InlineData("gust",          AttackType.Physical)]  // Normal (Gen 1; retyped to Flying in Gen 2)
    [InlineData("bite",          AttackType.Physical)]  // Normal (Gen 1; retyped to Dark in Gen 2 ⇒ was Special)
    [InlineData("wing-attack",   AttackType.Physical)]  // Flying
    [InlineData("vine-whip",     AttackType.Special)]   // Grass (Special in Gen 1)
    [InlineData("double-kick",   AttackType.Physical)]  // Fighting
    [InlineData("jump-kick",     AttackType.Physical)]  // Fighting
    [InlineData("rolling-kick",  AttackType.Physical)]  // Fighting
    [InlineData("slam",          AttackType.Physical)]  // Normal
    [InlineData("stomp",         AttackType.Physical)]  // Normal
    [InlineData("headbutt",      AttackType.Physical)]  // Normal
    [InlineData("horn-attack",   AttackType.Physical)]  // Normal
    [InlineData("tackle",        AttackType.Physical)]  // Normal
    [InlineData("body-slam",     AttackType.Physical)]  // Normal
    [InlineData("take-down",     AttackType.Physical)]  // Normal
    [InlineData("thrash",        AttackType.Physical)]  // Normal
    [InlineData("double-edge",   AttackType.Physical)]  // Normal
    [InlineData("fury-attack",   AttackType.Physical)]  // Normal
    [InlineData("horn-drill",    AttackType.Physical)]  // Normal
    [InlineData("poison-sting",  AttackType.Physical)]  // Poison (physical in Gen 1)
    [InlineData("acid",          AttackType.Physical)]  // Poison (physical in Gen 1)
    [InlineData("ember",         AttackType.Special)]   // Fire
    [InlineData("water-gun",     AttackType.Special)]   // Water
    [InlineData("ice-beam",      AttackType.Special)]   // Ice
    [InlineData("psybeam",       AttackType.Special)]   // Psychic
    // Status moves keep no damage category (Undefined) regardless of type.
    [InlineData("sand-attack",   AttackType.Undefined)]  // Normal in Gen 1 (retyped to Ground in Gen 2)
    [InlineData("tail-whip",     AttackType.Undefined)]
    public void MoveHasGen1PhysicalSpecialCategory(string moveName, AttackType expected)
        => Assert.Equal(expected, Move(moveName).AttackType);
}
