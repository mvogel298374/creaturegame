using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public sealed class Gen1BattleRules : IBattleRules
{
    /// <summary>Default singleton — uses the shared global RNG.</summary>
    public static readonly Gen1BattleRules Instance = new();

    private readonly IRandomSource _rng;

    /// <param name="rng">RNG source for the random rolls (sleep/binding turns, damage
    /// variance). Defaults to the shared global source; pass a <see cref="SeededRandomSource"/>
    /// for reproducible battles.</param>
    public Gen1BattleRules(IRandomSource? rng = null) => _rng = rng ?? SystemRandomSource.Instance;

    // Gen 1: only Fire-type moves that can inflict burn thaw a frozen target.
    // Fire Spin cannot burn, so it does not thaw — even though it is Fire-type.
    public bool CanThawFrozenTarget(Attack move) =>
        move.DamageType == DamageType.Fire && move.StatusEffect == StatusCondition.Burn;

    // Gen 1 freeze is permanent until hit by the right move; no random per-turn thaw.
    public int FreezeRandomThawPercent => 0;

    // Gen 1 damage roll: uniform integer in [217, 255], divided by 255.
    private const int VarianceMinRoll = 217;
    private const int VarianceMaxRoll = 255;

    public double RollDamageVariance() => _rng.Next(VarianceMinRoll, VarianceMaxRoll + 1) / 255.0;

    public (double Min, double Max) DamageVarianceRange =>
        (VarianceMinRoll / 255.0, VarianceMaxRoll / 255.0);

    // Gen 1 sleep lasts 1–7 turns.
    public int RollSleepTurns() => _rng.Next(1, 8);

    // Gen 1 confusion lasts 1–4 turns. The counter is one higher (2–5) because
    // StatusResolver decrements it before checking for clear (see RollConfusionTurns doc).
    public int RollConfusionTurns() => _rng.Next(2, 6);

    public RedundantConfuseAnnouncement RedundantConfusionAnnouncement =>
        RedundantConfuseAnnouncement.FailedGeneric;

    public StatCapAnnouncement StatStageCapAnnouncement => StatCapAnnouncement.NothingHappened;

    // Gen 1: a confused creature hits itself 50% of the time.
    public int ConfusionSelfHitPercent => 50;

    // Gen 1 Same-Type Attack Bonus.
    public double StabMultiplier => 1.5;

    // Gen 1 stores one effect chance per move, so every secondary effect kind reads the same column.
    public int GetSecondaryEffectChance(Attack move, SecondaryEffectKind effect) =>
        move.EffectChance ?? 100;

    // Gen 1: a 1–100 roll lands the secondary effect when it's <= the percent chance (chance 0
    // never procs, 100 always does). A simplification of Gen 1's true n/256 internal scale.
    public bool SecondaryHits(int chancePercent, IRandomSource rng) =>
        rng.Next(1, 101) <= chancePercent;

    // Gen 1 multi-hit distribution: 2 and 3 hits at 3/8 each, 4 and 5 hits at 1/8 each.
    public int RollMultiHitCount() =>
        _rng.Next(8) switch
        {
            0 or 1 or 2 => 2,
            3 or 4 or 5 => 3,
            6 => 4,
            _ => 5,
        };

    // Gen 1: Pay Day yields money equal to twice the user's level.
    public int PayDayCoinMultiplier => 2;

    // Gen 1 Struggle recoil: half the damage dealt to the target.
    public int CalculateStruggleRecoil(Creature source, int damageDealt) =>
        Math.Max(1, damageDealt / 2);

    // Gen 1: Burn and Poison each deal 1/16 max HP per turn.
    public int BurnDamageDenominator => 16;
    public int PoisonDamageDenominator => 16;

    // Gen 1: Leech Seed drains 1/16 max HP per turn.
    public int LeechSeedDrainDenominator => 16;

    // Gen 1: Paralysis quarters a paralyzed creature's effective Speed for turn order.
    public int ParalysisSpeedDivisor => 4;

    // Gen 1 Bad Poison (Toxic): counter/16 of max HP; no cap — counter increments each turn.
    public double BadPoisonDamageFraction(int toxicCounter) => toxicCounter / 16.0;

    // Gen 1: binding uses the multi-hit 2–5 odds (pokered effects.asm TrappingEffect).
    public int RollBindingTurns() =>
        _rng.Next(8) switch
        {
            0 or 1 or 2 => 2,
            3 or 4 or 5 => 3,
            6 => 4,
            _ => 5,
        };

    // Gen 1: a missed Jump Kick / Hi Jump Kick deals a flat 1 HP of crash damage to the user.
    public int CalculateCrashDamage(Creature user) => 1;

    // Gen 1: recoil moves deal 1/4 of the damage dealt back to the user (minimum 1).
    public int CalculateRecoilDamage(int damageDealt) => Math.Max(1, damageDealt / 4);

    // Gen 1: Thrash / Petal Dance lock the user in for 2 or 3 turns, then self-confuse.
    public int RollRampageTurns() => _rng.Next(2, 4);

    // Gen 1: Disable locks a move out for 1–8 turns (pokered DisableEffect: `and $7` / `inc a`).
    public int RollDisableTurns() => _rng.Next(8) + 1;

    // ── Move-specific damage quirks ──────────────────────────────────────────────

    // Gen 1: an OHKO fails when the target's in-battle Speed beats the user's (a Speed check, not level).
    public bool OneHitKoSucceeds(Creature user, Creature target) =>
        StatusResolver.EffectiveSpeed(user, this) >= StatusResolver.EffectiveSpeed(target, this);

    // Gen 1: a Normal or Fighting type qualifies; Bide's typeless unleash arrives as null and doesn't.
    public bool CounterQualifies(DamageType? lastDamageType) =>
        lastDamageType is DamageType.Normal or DamageType.Fighting;

    // Gen 1: Self-Destruct / Explosion halve the target's Defense before the damage calculation.
    public int SelfDestructDefenseDivisor => 2;

    // Gen 1: Rage raises the user's Attack by one stage each time it is hit while raging.
    public int RageAttackStagesPerHit => 1;

    // Gen 1: Recover / Soft-Boiled restore half of max HP.
    public double RecoverHealFraction => 0.5;

    // Gen 1: Reflect / Light Screen double the relevant defensive stat (crits ignore it).
    public int ScreenDefenseMultiplier => 2;

    // Gen 1: Bide commits the user for 2–3 turns, then unleashes double the damage absorbed.
    public int RollBideTurns() => _rng.Next(2, 4);

    public int BideDamageMultiplier => 2;

    public int RollPsywaveDamage(Creature source, BattleSide side, IRandomSource rng)
    {
        var (min, max) = PsywaveDamageRange(source, side);
        return rng.Next(min, max + 1);
    }

    // pokered core.asm: only the player's Psywave loop rejects a 0 roll.
    public (int Min, int Max) PsywaveDamageRange(Creature source, BattleSide side)
    {
        int min = side == BattleSide.Opponent ? 0 : 1;
        return (min, Math.Max(min, source.Level * 3 / 2 - 1));
    }

    // pokered effects.asm StatModifierDownEffect: enemy turn, non-link (we have no link battles).
    public bool StatDownHandicapApplies(BattleSide side, Attack move, bool isSecondary) =>
        side == BattleSide.Opponent;

    private const int StatDownHandicapFailBelow = 64;

    public bool RollStatDownHandicapFails() => _rng.Next(256) < StatDownHandicapFailBelow;

    // Gen 1: Rest forces the user asleep for exactly 2 turns (then it wakes and acts).
    public int RestSleepTurns => 2;

    // ── Type-based immunities ────────────────────────────────────────────────────

    // Narrower than pokered's same-type rule (GENERATION_SEAMS.md §2, Status by type).
    public bool CanReceiveStatus(Creature target, StatusCondition status, DamageType moveType) =>
        status switch
        {
            StatusCondition.Poison or StatusCondition.BadPoison => !HasType(
                target,
                DamageType.Poison
            ),
            StatusCondition.Burn => !HasType(target, DamageType.Fire),
            StatusCondition.Paralysis when moveType == DamageType.Normal => !HasType(
                target,
                DamageType.Normal
            ),
            _ => true,
        };

    // Thunder Wave is the only Gen 1 status move that consults the type chart.
    public bool PureStatusMoveChecksTypeImmunity(Attack move) =>
        move.StatusEffect == StatusCondition.Paralysis && move.DamageType == DamageType.Electric;

    // Gen 1: Grass-types are immune to Leech Seed.
    public bool CanBeLeechSeeded(Creature target) => !HasType(target, DamageType.Grass);

    // Gen 1: Roar/Whirlwind only end a wild battle; against a non-escapable foe they fail.
    public bool ForceFleeFailsVsTrainer => true;

    // Gen 1: a mid-turn faint ends the turn for both sides (Smogon RBY Mechanics Guide).
    public bool FaintEndsTurnImmediately => true;

    // Gen 1: Toxic reverts to regular Poison out of battle (the escalating counter is volatile); every
    // other major status carries unchanged.
    public StatusCondition CarryStatusOutOfBattle(StatusCondition status) =>
        status == StatusCondition.BadPoison ? StatusCondition.Poison : status;

    private static bool HasType(Creature c, DamageType type) => c.Type1 == type || c.Type2 == type;

    // ── Stat stages ────────────────────────────────────────────────────────────

    // Gen 1 battle-stat table: 2/(2+|n|) for n≤0, (2+n)/2 for n>0.
    public double GetStatMultiplier(int stage)
    {
        stage = Math.Clamp(stage, -6, 6);
        return stage <= 0 ? 2.0 / (2 - stage) : (2.0 + stage) / 2.0;
    }

    // Gen 1 accuracy/evasion table: 3/(3+|n|) for n≤0, (3+n)/3 for n>0.
    // Yields 0.333× at -6, 1.0× at 0, 3.0× at +6.
    public double GetAccuracyStageMultiplier(int stage)
    {
        stage = Math.Clamp(stage, -6, 6);
        return stage <= 0 ? 3.0 / (3 - stage) : (3.0 + stage) / 3.0;
    }

    // Gen 1: convert accuracy % to 0-255 scale, apply stage multipliers.
    // A roll of 255 always misses (1/256 bug) because the threshold caps at 255.
    public int GetHitThreshold(int accuracyPercent, int attackerAccStage, int defenderEvaStage)
    {
        int threshold = (int)Math.Floor(accuracyPercent * 255.0 / 100.0);
        double accMult = GetAccuracyStageMultiplier(attackerAccStage);
        double evaMult = GetAccuracyStageMultiplier(defenderEvaStage);
        return Math.Clamp((int)Math.Floor(threshold * accMult / evaMult), 0, 255);
    }

    // Gen 1: roll 0-255; roll >= threshold → miss. Roll 255 always misses for 100%-acc moves.
    public int AccuracyRollBound => 256;

    public bool XAccuracyBypassesAccuracyCheck => true;

    public bool RepeatFlagItemRefused => false;

    public bool CappedXItemRefused => false;

    public bool BoosterItemAnnouncesEffect => false;

    public HitRollOrder HitRollOrder => HitRollOrder.CritAndDamageThenAccuracy;

    public bool MultiHitRepeatsFirstStrike => true;

    public bool FixedDamageChecksTypeImmunity => false;

    public bool DamageRollSkippedBelowTwo => true;

    public bool StatusMoveImmunityBeforeAccuracy => true;

    public bool CrashOnTypeImmunity => true;

    // ── Stat selection ─────────────────────────────────────────────────────────

    // Gen 1: Physical moves use Attack/Defense; Special moves use the combined Special stat.
    public int GetOffensiveStat(Creature attacker, AttackType moveType) =>
        moveType == AttackType.Physical ? attacker.Attributes.Attack : attacker.Attributes.Special;

    public int GetDefensiveStat(Creature defender, AttackType moveType) =>
        moveType == AttackType.Physical ? defender.Attributes.Defense : defender.Attributes.Special;

    // pokered wram.asm: one Special mod per side (wPlayerMonSpecialMod / wEnemyMonSpecialMod).
    public StageStat OffensiveStage(AttackType moveType) =>
        moveType == AttackType.Physical ? StageStat.Attack : StageStat.Special;

    public StageStat DefensiveStage(AttackType moveType) =>
        moveType == AttackType.Physical ? StageStat.Defense : StageStat.Special;

    // ── Critical hits ──────────────────────────────────────────────────────────

    // Gen 1: normal = floor(BaseSpeed/2)/256; high-crit = min(floor(BaseSpeed/2)*8, 255)/256.
    // BaseSpeed is the raw unmodified base speed stat.
    public double GetCritChance(Creature attacker, Attack move)
    {
        double numerator = Math.Floor(attacker.BaseSpeed / 2.0);
        if (move.IsHighCrit)
            numerator = Math.Min(numerator * 8, 255);
        // Gen 1 bug: Focus Energy was meant to quadruple the crit rate but instead quarters it.
        if (attacker.Battle.HasFocusEnergy)
            numerator = Math.Floor(numerator / 4.0);
        return numerator / 256.0;
    }

    public double CritMultiplier => 2.0;

    // Gen 1: crits use raw computed stats, bypassing stages and the Burn Attack penalty.
    public bool CritIgnoresStatStages => true;

    // pokered engine/battle/experience.asm: GEN_DIFFERENCES.md → Experience.
    public ExperienceRules Experience { get; } =
        new(LevelDivisor: 7, Split: ExperienceSplit.Yields, Boosts: [ExperienceBoost.Trainer]);
}
