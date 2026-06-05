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
    [InlineData("hyper-beam",    AttackType.Physical)]  // Normal (Special in modern gens — the classic Gen 1 split case)
    [InlineData("peck",          AttackType.Physical)]  // Flying
    [InlineData("submission",    AttackType.Physical)]  // Fighting
    [InlineData("ember",         AttackType.Special)]   // Fire
    [InlineData("water-gun",     AttackType.Special)]   // Water
    [InlineData("ice-beam",      AttackType.Special)]   // Ice
    [InlineData("psybeam",       AttackType.Special)]   // Psychic
    [InlineData("thunderbolt",   AttackType.Special)]   // Electric
    [InlineData("dragon-rage",   AttackType.Special)]   // Dragon (fixed-damage, still Special by type)
    [InlineData("fire-spin",     AttackType.Special)]   // Fire
    [InlineData("rock-throw",    AttackType.Physical)]  // Rock
    [InlineData("earthquake",    AttackType.Physical)]  // Ground
    [InlineData("fissure",       AttackType.Physical)]  // Ground (OHKO, still Physical by type)
    [InlineData("dig",           AttackType.Physical)]  // Ground (two-turn, still Physical by type)
    [InlineData("confusion",     AttackType.Special)]   // Psychic
    [InlineData("psychic",       AttackType.Special)]   // Psychic
    [InlineData("quick-attack",  AttackType.Physical)]  // Normal (priority move, still Physical by type)
    [InlineData("rage",          AttackType.Physical)]  // Normal
    // Status moves keep no damage category (Undefined) regardless of type.
    [InlineData("toxic",         AttackType.Undefined)]  // Poison status move
    [InlineData("hypnosis",      AttackType.Undefined)]  // Psychic status move
    [InlineData("agility",       AttackType.Undefined)]  // Psychic status move
    [InlineData("teleport",      AttackType.Undefined)]  // Psychic status move
    [InlineData("night-shade",   AttackType.Physical)]  // Ghost (level-based, still Physical by type)
    [InlineData("mimic",         AttackType.Undefined)]  // Normal status move
    [InlineData("screech",       AttackType.Undefined)]  // Normal status move
    [InlineData("recover",       AttackType.Undefined)]  // Normal status (heal) move
    [InlineData("minimize",      AttackType.Undefined)]  // Normal status move
    [InlineData("confuse-ray",   AttackType.Undefined)]  // Ghost status move
    [InlineData("withdraw",      AttackType.Undefined)]  // Water status move
    [InlineData("defense-curl",  AttackType.Undefined)]  // Normal status move
    [InlineData("barrier",       AttackType.Undefined)]  // Psychic status move
    [InlineData("light-screen",  AttackType.Undefined)]  // Psychic status move
    [InlineData("haze",          AttackType.Undefined)]  // Ice status move
    [InlineData("reflect",       AttackType.Undefined)]  // Psychic status move
    [InlineData("focus-energy",  AttackType.Undefined)]  // Normal status move
    [InlineData("metronome",     AttackType.Undefined)]  // Normal status move
    [InlineData("mirror-move",   AttackType.Undefined)]  // Flying status move
    [InlineData("self-destruct", AttackType.Physical)]  // Normal (Self-Destruct, still Physical by type)
    [InlineData("sand-attack",   AttackType.Undefined)]  // Normal in Gen 1 (retyped to Ground in Gen 2)
    [InlineData("tail-whip",     AttackType.Undefined)]
    [InlineData("string-shot",   AttackType.Undefined)]  // Bug status move
    [InlineData("thunder-wave",  AttackType.Undefined)]  // Electric status move
    public void MoveHasGen1PhysicalSpecialCategory(string moveName, AttackType expected)
        => Assert.Equal(expected, Move(moveName).AttackType);
}
