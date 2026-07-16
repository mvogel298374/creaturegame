using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Tests.Unit;

/// <summary>
/// <see cref="Creature.Types"/> — the one place that projects the <see cref="Creature.Type1"/>/
/// <see cref="Creature.Type2"/> slot pair into an iterable typing. Generation-invariant (a creature's typing is
/// two optional slots in every generation; only the type *chart* is gen-variable). Two live callers read it —
/// the opening biome matchup sweep (`BiomeChoiceEvent`) and the acquisition offer's wire payload — so the
/// dual-type path here is what stops a dropped second type from silently corrupting both at once.
/// </summary>
public class CreatureTypingTests
{
    [Fact]
    public void Types_ForADualTypedCreature_ReturnsBothTypes_InSlotOrder()
    {
        var creature = new Creature("Bulbasaur")
        {
            Type1 = DamageType.Grass,
            Type2 = DamageType.Poison,
        };

        Assert.Equal([DamageType.Grass, DamageType.Poison], creature.Types);
    }

    [Fact]
    public void Types_ForASingleTypedCreature_ReturnsOnlyTheFirstSlot()
    {
        var creature = new Creature("Pikachu") { Type1 = DamageType.Electric, Type2 = null };

        Assert.Equal([DamageType.Electric], creature.Types);
    }

    [Fact]
    public void Types_ForAnUntypedCreature_IsEmpty_NotAListOfNulls()
    {
        var creature = new Creature("Blank") { Type1 = null, Type2 = null };

        Assert.Empty(creature.Types);
    }
}
