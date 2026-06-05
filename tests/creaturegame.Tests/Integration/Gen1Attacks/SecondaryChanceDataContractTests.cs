using creaturegame.Attacks;
using creaturegame.Creatures;
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

    // Toxic badly-poisons; PokeAPI reports its ailment as plain "poison", so the importer promotes
    // it to BadPoison. Without this pin, a re-import would silently downgrade Toxic to regular Poison.
    [Fact]
    public void ToxicBadlyPoisonsInGen1()
        => Assert.Equal(StatusCondition.BadPoison, Move("toxic").StatusEffect);

    // Rage carries the Gen 1 Rage mechanic (lock-in + Attack-on-hit), mapped by name in the importer.
    // PokeAPI's ailment data can't express it; without this pin a re-import that drops the name-match
    // would silently leave Rage a plain 20-power Normal move (the behaviour test would fail obscurely).
    [Fact]
    public void RageHasRageMoveEffect()
        => Assert.Equal(MoveEffect.Rage, Move("rage").Effect);

    // Recover/Mimic mechanics and Night Shade's level-based category are importer name/ID mappings
    // PokeAPI can't express — pin them so a re-import can't silently revert them to plain status moves.
    [Fact]
    public void RecoverHasHealMoveEffect()
        => Assert.Equal(MoveEffect.Heal, Move("recover").Effect);

    // Soft-Boiled shares Recover's importer clause (both → Heal). Its behaviour coverage lands in its
    // own batch, but pin the mapping now so the shared clause can't silently regress for either move.
    [Fact]
    public void SoftBoiledHasHealMoveEffect()
        => Assert.Equal(MoveEffect.Heal, Move("soft-boiled").Effect);

    [Fact]
    public void MimicHasMimicMoveEffect()
        => Assert.Equal(MoveEffect.Mimic, Move("mimic").Effect);

    [Fact]
    public void NightShadeIsLevelBased()
        => Assert.Equal(DamageCategory.LevelBased, Move("night-shade").DamageCategory);

    // Batch-12 move-effect mappings the importer applies by name (PokeAPI can't express these Gen 1
    // mechanics). Pin them so a re-import that drops a clause can't silently neuter the move.
    [Theory]
    [InlineData("reflect",      MoveEffect.Reflect)]
    [InlineData("light-screen", MoveEffect.LightScreen)]
    [InlineData("focus-energy", MoveEffect.FocusEnergy)]
    [InlineData("bide",         MoveEffect.Bide)]
    [InlineData("mirror-move",  MoveEffect.MirrorMove)]
    [InlineData("haze",         MoveEffect.Haze)]
    [InlineData("metronome",    MoveEffect.Metronome)]
    public void MoveHasItsGen1Effect(string move, MoveEffect effect)
        => Assert.Equal(effect, Move(move).Effect);

    // Screech lowers the foe's Defense by two stages in every gen (the others in its family are −1).
    [Fact]
    public void ScreechLowersFoeDefenseByTwo()
    {
        var move = Move("screech");
        Assert.Equal(StageStat.Defense, move.StatEffectStat);
        Assert.Equal(StageTarget.Foe,   move.StatEffectTarget);
        Assert.Equal(-2,                move.StatEffectDelta);
    }
}
