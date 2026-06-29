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
    [InlineData("thunder", 10)] // Gen 1 paralysis 10% (modern 30%)
    [InlineData("bite", 10)] // Gen 1 flinch 10% (modern 30%)
    [InlineData("low-kick", 30)] // Gen 1 flinch 30% (modern: weight-based, none)
    [InlineData("poison-sting", 20)] // Gen 1 poison 20% (modern 30%)
    [InlineData("acid", 33)] // acid overrides EffectChance too (33%), not just StatEffectChance
    public void EffectChanceMatchesGen1(string move, int chance) =>
        Assert.Equal(chance, Move(move).EffectChance);

    // Stat-drop secondary chance — lives in StatEffectChance.
    [Theory]
    [InlineData("acid", 33)] // Gen 1 −1 Defense 33% (modern −1 Sp.Def 10%)
    [InlineData("aurora-beam", 33)] // Gen 1 −1 Attack 33% (modern 10%)
    [InlineData("bubble-beam", 33)] // Gen 1 −1 Speed 33% (modern 10%)
    public void StatEffectChanceMatchesGen1(string move, int chance) =>
        Assert.Equal(chance, Move(move).StatEffectChance);

    // Stat-drop magnitude that changed between gens — pin the whole row (stat/target/delta), since the
    // override only sets the delta and the rest rides on PokeAPI's stat_changes mapping.
    [Fact]
    public void StringShotLowersFoeSpeedByOneInGen1()
    {
        var move = Move("string-shot");
        Assert.Equal(StageStat.Speed, move.StatEffectStat);
        Assert.Equal(StageTarget.Foe, move.StatEffectTarget);
        Assert.Equal(-1, move.StatEffectDelta); // modern: −2
    }

    // Growth raises the combined Special in Gen 1, not Attack.
    [Fact]
    public void GrowthRaisesSpecialInGen1() =>
        Assert.Equal(StageStat.Special, Move("growth").StatEffectStat);

    // Toxic badly-poisons; PokeAPI reports its ailment as plain "poison", so the importer promotes
    // it to BadPoison. Without this pin, a re-import would silently downgrade Toxic to regular Poison.
    [Fact]
    public void ToxicBadlyPoisonsInGen1() =>
        Assert.Equal(StatusCondition.BadPoison, Move("toxic").StatusEffect);

    // Rage carries the Gen 1 Rage mechanic (lock-in + Attack-on-hit), mapped by name in the importer.
    // PokeAPI's ailment data can't express it; without this pin a re-import that drops the name-match
    // would silently leave Rage a plain 20-power Normal move (the behaviour test would fail obscurely).
    [Fact]
    public void RageHasRageMoveEffect() => Assert.Equal(MoveEffect.Rage, Move("rage").Effect);

    // Roar/Whirlwind map by name to ForceFlee (end a wild battle); PokeAPI's "force switch" ailment can't
    // express the Gen 1 semantics. Pin the mapping AND that neither carries a status — TryApplyStatus runs
    // before the effect, so a re-import that resolved a non-None StatusEffect would leak a status on the flee.
    [Theory]
    [InlineData("roar")]
    [InlineData("whirlwind")]
    public void RoarAndWhirlwindForceFleeWithoutStatus(string move)
    {
        Assert.Equal(MoveEffect.ForceFlee, Move(move).Effect);
        Assert.Equal(StatusCondition.None, Move(move).StatusEffect);
    }

    // Recover/Mimic mechanics and Night Shade's level-based category are importer name/ID mappings
    // PokeAPI can't express — pin them so a re-import can't silently revert them to plain status moves.
    [Fact]
    public void RecoverHasHealMoveEffect() => Assert.Equal(MoveEffect.Heal, Move("recover").Effect);

    // Soft-Boiled shares Recover's importer clause (both → Heal). Its behaviour coverage lands in its
    // own batch, but pin the mapping now so the shared clause can't silently regress for either move.
    [Fact]
    public void SoftBoiledHasHealMoveEffect() =>
        Assert.Equal(MoveEffect.Heal, Move("soft-boiled").Effect);

    [Fact]
    public void MimicHasMimicMoveEffect() => Assert.Equal(MoveEffect.Mimic, Move("mimic").Effect);

    [Fact]
    public void NightShadeIsLevelBased() =>
        Assert.Equal(DamageCategory.LevelBased, Move("night-shade").DamageCategory);

    // Batch-12 move-effect mappings the importer applies by name (PokeAPI can't express these Gen 1
    // mechanics). Pin them so a re-import that drops a clause can't silently neuter the move.
    [Theory]
    [InlineData("reflect", MoveEffect.Reflect)]
    [InlineData("light-screen", MoveEffect.LightScreen)]
    [InlineData("focus-energy", MoveEffect.FocusEnergy)]
    [InlineData("bide", MoveEffect.Bide)]
    [InlineData("mirror-move", MoveEffect.MirrorMove)]
    [InlineData("haze", MoveEffect.Haze)]
    [InlineData("metronome", MoveEffect.Metronome)]
    public void MoveHasItsGen1Effect(string move, MoveEffect effect) =>
        Assert.Equal(effect, Move(move).Effect);

    // Screech lowers the foe's Defense by two stages in every gen (the others in its family are −1).
    [Fact]
    public void ScreechLowersFoeDefenseByTwo()
    {
        var move = Move("screech");
        Assert.Equal(StageStat.Defense, move.StatEffectStat);
        Assert.Equal(StageTarget.Foe, move.StatEffectTarget);
        Assert.Equal(-2, move.StatEffectDelta);
    }

    // ── Batch 13 layer-2 facts ─────────────────────────────────────────────────────────────────
    // Fire Blast burned at 30% in Gen 1 (reduced to 10% from Gen 2). PokeAPI reports the modern 10%.
    [Fact]
    public void FireBlastBurnChanceIsThirtyInGen1() =>
        Assert.Equal(30, Move("fire-blast").EffectChance);

    // Waterfall had no secondary effect in Gen 1–3; the 20% flinch was added in Gen 4. The importer
    // strips the modern flinch back off — pin both the cleared effect and the null chance.
    [Fact]
    public void WaterfallHasNoSecondaryEffectInGen1()
    {
        var move = Move("waterfall");
        Assert.Equal(MoveEffect.None, move.Effect);
        Assert.Null(move.EffectChance);
    }

    // Gen 1 Skull Bash is a plain two-turn charge move — mapped by name in the importer (PokeAPI's
    // meta can't express the charge). It does NOT raise Defense on the charge turn (that's Gen 2+),
    // so it carries no stat-stage effect; pin both so a re-import can't silently regress either.
    [Fact]
    public void SkullBashIsTwoTurnWithNoDefenseBoostInGen1()
    {
        var move = Move("skull-bash");
        Assert.Equal(MoveEffect.TwoTurn, move.Effect);
        // No Gen 1 charge-turn Defense boost (that's Gen 2+) and no stale secondary chance: guard the
        // whole stat-change row + EffectChance so a future PokeAPI stat_changes entry can't sneak in.
        Assert.Null(move.StatEffectStat);
        Assert.Null(move.StatEffectDelta);
        Assert.Null(move.StatEffectChance);
        Assert.Null(move.EffectChance);
    }

    // ── Batch 14 mappings ──────────────────────────────────────────────────────────────────────
    // High Jump Kick crashes on a miss like Jump Kick (importer name-match → Crash). Dream Eater is a
    // sleep-gated drain — its DreamEater effect is a name mapping, and its 50% drain rides on the Drain
    // category. Pin both so a re-import that drops a clause can't silently neuter either move.
    [Fact]
    public void HighJumpKickCrashesOnMissInGen1() =>
        Assert.Equal(MoveEffect.Crash, Move("high-jump-kick").Effect);

    [Fact]
    public void DreamEaterIsASleepGatedDrainInGen1()
    {
        var move = Move("dream-eater");
        Assert.Equal(MoveEffect.DreamEater, move.Effect);
        Assert.Equal(DamageCategory.Drain, move.DamageCategory);
    }

    // ── Batch 15 layer-2 facts ─────────────────────────────────────────────────────────────────
    // Bubble and Constrict both lowered Speed at 33% in Gen 1 (modern: 10%). PokeAPI reports the
    // modern chance and can't express the Gen 1 value via past_values, so it's an importer override —
    // pin both the stat-drop chance and the EffectChance the override sets alongside it.
    [Theory]
    [InlineData("bubble")]
    [InlineData("constrict")]
    public void Gen1ThirtyThreePercentSpeedDropChance(string move)
    {
        Assert.Equal(33, Move(move).StatEffectChance);
        Assert.Equal(33, Move(move).EffectChance);
        Assert.Equal(StageStat.Speed, Move(move).StatEffectStat);
    }

    // Dizzy Punch had no secondary effect in Gen 1 (the 20% confusion was added in Gen 5). The importer
    // strips the modern secondary back off — pin both the cleared effect and the null chance.
    [Fact]
    public void DizzyPunchHasNoSecondaryEffectInGen1()
    {
        var move = Move("dizzy-punch");
        Assert.Equal(MoveEffect.None, move.Effect);
        Assert.Null(move.EffectChance);
    }

    // Gen 1–2 Sky Attack is a plain two-turn charge (mapped by name → TwoTurn). The 30% flinch was
    // added in Gen 3; the importer clears the stale chance. Pin the TwoTurn mapping and the null chance.
    [Fact]
    public void SkyAttackIsTwoTurnWithNoFlinchInGen1()
    {
        var move = Move("sky-attack");
        Assert.Equal(MoveEffect.TwoTurn, move.Effect);
        Assert.Null(move.EffectChance);
    }

    // Psywave's variable-damage category and Splash's no-op effect are importer ID/name mappings PokeAPI
    // can't express — pin them so a re-import that drops a clause can't silently revert either move.
    [Fact]
    public void PsywaveUsesTheVariableDamageCategory() =>
        Assert.Equal(DamageCategory.Psywave, Move("psywave").DamageCategory);

    [Fact]
    public void SplashIsAGen1NoOp() => Assert.Equal(MoveEffect.Splash, Move("splash").Effect);

    // ── Batch 16 facts ─────────────────────────────────────────────────────────────────────────
    // Rock Slide had no secondary effect in Gen 1; the 30% flinch was added in Gen 2. The importer
    // strips the modern flinch back off — pin both the cleared effect and the null chance.
    [Fact]
    public void RockSlideHasNoFlinchInGen1()
    {
        var move = Move("rock-slide");
        Assert.Equal(MoveEffect.None, move.Effect);
        Assert.Null(move.EffectChance);
    }

    // Bonemerang strikes exactly twice (fixed-count multi-hit) — an importer name mapping (→ MultiHit)
    // plus the fixed MultiHitCount. PokeAPI's meta can't express the fixed-2; pin both so a re-import
    // that drops a clause can't silently revert it to a single hit or a 2–5 roll.
    [Fact]
    public void BonemerangIsAFixedTwoHitInGen1()
    {
        var move = Move("bonemerang");
        Assert.Equal(MoveEffect.MultiHit, move.Effect);
        Assert.Equal(2, move.MultiHitCount);
    }

    // Rest's heal+sleep effect is an importer name mapping PokeAPI can't express — pin it so a
    // re-import that drops the clause can't silently leave Rest a plain do-nothing status move.
    // Also pin StatusEffect == None: Rest's sleep is self-inflicted by the engine, NOT a foe-directed
    // ailment. PokeAPI reports Rest's ailment as "none" (correct for Gen 1), so no override is needed —
    // but TryApplyStatus runs before the Rest handler, so if a re-import ever set StatusEffect = Sleep
    // here, Rest would wrongly sleep the FOE at 100%. This pin fails loudly if that ever drifts.
    [Fact]
    public void RestHasRestMoveEffectAndNoFoeAilment()
    {
        var move = Move("rest");
        Assert.Equal(MoveEffect.Rest, move.Effect);
        Assert.Equal(StatusCondition.None, move.StatusEffect);
    }

    // ── Batch 17 facts ─────────────────────────────────────────────────────────────────────────
    // Tri Attack had no secondary effect in Gen 1; the 20% random burn/freeze/paralysis was added in
    // Gen 2. The importer strips the modern secondary back off — pin the cleared effect + null chance.
    [Fact]
    public void TriAttackHasNoSecondaryEffectInGen1()
    {
        var move = Move("tri-attack");
        Assert.Equal(MoveEffect.None, move.Effect);
        Assert.Null(move.EffectChance);
    }

    // Super Fang's half-current-HP category is an importer ID mapping PokeAPI can't express via meta;
    // Substitute's effect is a name mapping. Pin both so a re-import that drops a clause can't silently
    // revert Super Fang to a 0-power Normal move or neuter Substitute.
    [Fact]
    public void SuperFangUsesTheSuperFangCategory() =>
        Assert.Equal(DamageCategory.SuperFang, Move("super-fang").DamageCategory);

    [Fact]
    public void SubstituteHasSubstituteMoveEffect() =>
        Assert.Equal(MoveEffect.Substitute, Move("substitute").Effect);

    // ── Identity/type-mutation batch (Transform + Conversion) ────────────────────────────────────
    // Both are importer name mappings PokeAPI's meta can't express — Transform copies the foe's
    // identity, Conversion copies its types. Pin both effects so a re-import that drops a clause can't
    // silently leave either a do-nothing 0-power status move (the behaviour tests would then fail
    // obscurely instead of pointing at the data). Also pin StatusEffect == None on both: like Rest,
    // these are self-affecting moves, and TryApplyStatus runs BEFORE the move-effect handler keyed only
    // on attack.StatusEffect — so if a re-import ever set a non-None ailment here, the move would
    // wrongly afflict the FOE at 100% before the Transform/Conversion handler ran. This pin fails
    // loudly if that ever drifts.
    [Theory]
    [InlineData("transform", MoveEffect.Transform)]
    [InlineData("conversion", MoveEffect.Conversion)]
    public void IdentityMutationMoveHasItsEffectAndNoFoeAilment(string move, MoveEffect effect)
    {
        Assert.Equal(effect, Move(move).Effect);
        Assert.Equal(StatusCondition.None, Move(move).StatusEffect);
    }
}
