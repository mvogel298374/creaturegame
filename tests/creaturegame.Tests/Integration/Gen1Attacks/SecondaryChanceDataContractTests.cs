using creaturegame.Attacks;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Pins the importer's <b>layer-2 secondary-chance overrides</b> — the Gen 1 values PokeAPI reports
/// at their modern numbers and can't express via <c>past_values</c> (see DATA_IMPORT §5.5). Without
/// this, a re-import could silently restore the modern chance and every behaviour test (which forces
/// the roll) would stay green. Guards the imported <c>moves.db</c> rows directly.
/// </summary>
[Collection(MovesCollection.Name)]
public class SecondaryChanceDataContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    // Damaging-move secondary chance (status/flinch) — lives in EffectChance.
    [Theory]
    [InlineData("thunder", 10)]        // Gen 1 paralysis 10% (modern 30%)
    [InlineData("bite", 10)]           // Gen 1 flinch 10% (modern 30%)
    [InlineData("low-kick", 30)]       // Gen 1 flinch 30% (modern: weight-based, none)
    [InlineData("poison-sting", 20)]   // Gen 1 poison 20% (modern 30%)
    [InlineData("acid", 33)]           // acid overrides EffectChance too (33%), not just StatEffectChance
    public void EffectChanceMatchesGen1(string move, int chance)
        => Assert.Equal(chance, Move(move).EffectChance);

    // Stat-drop secondary chance — lives in StatEffectChance.
    [Theory]
    [InlineData("acid", 33)]           // Gen 1 −1 Defense 33% (modern −1 Sp.Def 10%)
    [InlineData("aurora-beam", 33)]    // Gen 1 −1 Attack 33% (modern 10%)
    [InlineData("bubble-beam", 33)]    // Gen 1 −1 Speed 33% (modern 10%)
    public void StatEffectChanceMatchesGen1(string move, int chance)
        => Assert.Equal(chance, Move(move).StatEffectChance);

    // Stat-drop magnitude that changed between gens — pin the whole row (stat/target/delta), since the
    // override only sets the delta and the rest rides on PokeAPI's stat_changes mapping.
    [Fact]
    public void StringShotLowersFoeSpeedByOneInGen1()
    {
        var move = Move("string-shot");
        Assert.Equal(StageStat.Speed,   move.StatEffectStat);
        Assert.Equal(StageTarget.Foe,   move.StatEffectTarget);
        Assert.Equal(-1,                move.StatEffectDelta);   // modern: −2
    }

    // Growth raises the combined Special in Gen 1, not Attack.
    [Fact]
    public void GrowthRaisesSpecialInGen1()
        => Assert.Equal(StageStat.Special, Move("growth").StatEffectStat);
}
