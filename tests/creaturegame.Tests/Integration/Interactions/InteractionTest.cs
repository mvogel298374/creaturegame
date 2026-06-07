using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Tests.Integration.Gen1Attacks;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Shared base for the full-battle interaction probes: gives access to the moves DB (via
/// <see cref="Gen1MoveContract"/>) and a one-liner for building a creature with a named moveset.
/// </summary>
public abstract class InteractionTest(MovesFixture moves) : Gen1MoveContract(moves)
{
    protected Creature Mon(
        string name,
        int hp,
        int attack,
        int speed,
        DamageType type1 = DamageType.Normal,
        params string[] moveNames
    )
    {
        var c = TestCreatures.Make(name, type1: type1, hp: hp, attack: attack, speed: speed);
        foreach (var n in moveNames)
            c.AddAttack(Move(n));
        return c;
    }
}
