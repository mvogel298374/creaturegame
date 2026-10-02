using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Web.Battle;

namespace creaturegame.Tests.Unit;

/// <summary>
/// <see cref="EncounterFactory.MachineFloorOrThrow"/> — the Strong/Boss tiers' TM/HM floor policy
/// (<c>ENCOUNTER_DESIGN.md</c> §3.5). A move with a floor returns it; a TM/HM move with none (a stale
/// <c>moves.db</c>) throws rather than silently passing ungated. Driven through the real selector so the throw is
/// proven to be reached for a Machine row, and not for a level-up row.
/// </summary>
public class EncounterFactoryMachineFloorTests
{
    private static Attack Move(int id, int? minLevel) =>
        new("move" + id, "")
        {
            Id = id,
            BaseDamage = 50,
            DamageType = DamageType.Normal,
            MinLevel = minLevel,
        };

    [Fact]
    public void MoveWithAFloor_ReturnsIt()
    {
        Assert.Equal(42, EncounterFactory.MachineFloorOrThrow(Move(1, 42)));
    }

    [Fact]
    public void MoveWithNoFloor_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EncounterFactory.MachineFloorOrThrow(Move(1, null))
        );
    }

    [Fact]
    public void StrongTier_MachineRowWithNoFloor_ThrowsThroughTheSelector()
    {
        var moves = new[] { Move(1, null) };
        var learnset = new[]
        {
            new PokemonLearnset
            {
                MoveId = 1,
                Method = LearnMethod.Machine,
                Generation = 1,
            },
        };

        Assert.Throws<InvalidOperationException>(() =>
            LearnsetMoveSelector.SelectWithFallback(
                MoveSelectionStrategy.Optimal,
                learnset,
                moves,
                level: 50,
                DamageType.Normal,
                null,
                EncounterFactory.MachineFloorOrThrow
            )
        );
    }

    [Fact]
    public void StrongTier_LevelUpRowWithNoFloor_DoesNotThrow()
    {
        // A level-up move has no TM floor and never consults the delegate.
        var moves = new[] { Move(1, null) };
        var learnset = new[]
        {
            new PokemonLearnset
            {
                MoveId = 1,
                LearnLevel = 1,
                Generation = 1,
            },
        };

        var result = LearnsetMoveSelector.SelectWithFallback(
            MoveSelectionStrategy.Optimal,
            learnset,
            moves,
            level: 5,
            DamageType.Normal,
            null,
            EncounterFactory.MachineFloorOrThrow
        );

        Assert.Single(result);
    }
}
