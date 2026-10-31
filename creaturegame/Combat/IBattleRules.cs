using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>The kind of secondary effect a move can roll for; see <see cref="IBattleRules.GetSecondaryEffectChance"/>.</summary>
public enum SecondaryEffectKind
{
    Status,
    Flinch,
    Confuse,
    StatStage,
}

/// <summary>How a dedicated confusion move announces an already-confused target. See GENERATION_SEAMS.md §2.</summary>
public enum RedundantConfuseAnnouncement
{
    /// <summary>The generic "But it failed!" (pokered <c>ConditionalPrintButItFailed</c>). Gen 1.</summary>
    FailedGeneric,

    /// <summary>A message naming the redundancy ("… is already confused!").</summary>
    AlreadyConfused,
}

/// <summary>How a primary stat move announces a stat already at the ±6 cap. See GENERATION_SEAMS.md §2.</summary>
public enum StatCapAnnouncement
{
    /// <summary>The generic "Nothing happened!" (pokered <c>PrintNothingHappenedText</c>). Gen 1.</summary>
    NothingHappened,
}

/// <summary>Where a move's crit and damage rolls fall relative to its accuracy roll. See GENERATION_SEAMS.md §2.</summary>
public enum HitRollOrder
{
    /// <summary>Crit and damage first, then accuracy; a 0× matchup outranks a miss. Gen 1.</summary>
    CritAndDamageThenAccuracy,

    /// <summary>Accuracy first; a miss ends the move before any crit or damage roll.</summary>
    AccuracyThenCritAndDamage,
}

public interface IBattleRules
{
    /// <summary>Whether the given move thaws a frozen target when it hits.</summary>
    bool CanThawFrozenTarget(Attack move);

    /// <summary>Per-turn chance (0–100) for a frozen Pokémon to thaw on its own at the start of its turn.</summary>
    int FreezeRandomThawPercent { get; }

    /// <summary>Same-Type Attack Bonus multiplier.</summary>
    double StabMultiplier { get; }

    /// <summary>Percent chance (0–100) that a confused creature hits itself instead of acting.</summary>
    int ConfusionSelfHitPercent { get; }

    /// <summary>How many times a multi-hit move strikes when it connects; drawn once per use.</summary>
    int RollMultiHitCount();

    /// <summary>Pay Day's money per user level, picked up after the battle.</summary>
    int PayDayCoinMultiplier { get; }

    /// <summary>The percent chance (0–100) a move's secondary effect applies on hit. See GENERATION_SEAMS.md §5.0.</summary>
    int GetSecondaryEffectChance(Attack move, SecondaryEffectKind effect);

    /// <summary>Whether a secondary effect with <paramref name="chancePercent"/> procs, drawn from the caller's <paramref name="rng"/>.</summary>
    bool SecondaryHits(int chancePercent, IRandomSource rng);

    /// <summary>The random damage multiplier for one hit.</summary>
    double RollDamageVariance();

    /// <summary>
    /// The inclusive bounds <see cref="RollDamageVariance"/> draws between — the single source for both the roll
    /// and the Dev Mode damage-range display, so the two can't disagree. Gen 1: 217/255 – 255/255.
    /// </summary>
    (double Min, double Max) DamageVarianceRange { get; }

    /// <summary>The number of turns the target sleeps, drawn each time Sleep is applied.</summary>
    int RollSleepTurns();

    /// <summary>
    /// Returns the confusion counter set when a creature becomes confused. Note this is the raw
    /// counter, one higher than the number of self-hit turns because <see cref="StatusResolver"/>
    /// decrements before its cleared-check. Gen 1: 2–5 (≈1–4 turns of confusion).
    /// </summary>
    int RollConfusionTurns();

    /// <summary>
    /// How a dedicated confusion move announces hitting an already-confused target — see
    /// <see cref="RedundantConfuseAnnouncement"/>. Gen 1: <see cref="RedundantConfuseAnnouncement.FailedGeneric"/>
    /// ("But it failed!"). The counter is never re-rolled regardless (gen-agnostic); this only picks the message.
    /// </summary>
    RedundantConfuseAnnouncement RedundantConfusionAnnouncement { get; }

    /// <summary>
    /// How a PRIMARY stat move announces hitting a stat already at the ±6 cap — see
    /// <see cref="StatCapAnnouncement"/>. Gen 1: <see cref="StatCapAnnouncement.NothingHappened"/> ("Nothing
    /// happened!"). The ±6 clamp is gen-agnostic; this only picks the message. A secondary stat drop on a
    /// damaging move stays silent regardless and never consults this.
    /// </summary>
    StatCapAnnouncement StatStageCapAnnouncement { get; }

    /// <summary>The recoil damage dealt to a Struggle user.</summary>
    int CalculateStruggleRecoil(Creature source, int damageDealt);

    /// <summary>Divisor of max HP for end-of-turn Burn damage (16 → 1/16).</summary>
    int BurnDamageDenominator { get; }

    /// <summary>Divisor of max HP for end-of-turn Poison damage (16 → 1/16).</summary>
    int PoisonDamageDenominator { get; }

    /// <summary>Divisor of the seeded creature's max HP for the Leech Seed drain; its own rule, not Poison's.</summary>
    int LeechSeedDrainDenominator { get; }

    /// <summary>Divisor of a paralyzed creature's effective Speed for turn order (<see cref="StatusResolver.EffectiveSpeed"/>).</summary>
    int ParalysisSpeedDivisor { get; }

    /// <summary>
    /// Returns the fraction of max HP dealt by Bad Poison (Toxic) on the given counter tick.
    /// Gen 1: counter/16 — escalates each turn with no cap (counter starts at 1, increments after damage).
    /// </summary>
    double BadPoisonDamageFraction(int toxicCounter);

    // ── Stat stages ────────────────────────────────────────────────────────────

    /// <summary>The damage-stat multiplier for a stage in [-6, +6].</summary>
    double GetStatMultiplier(int stage);

    /// <summary>
    /// Returns the accuracy/evasion multiplier for a stage in [-6, +6].
    /// Gen 1: stage≤0 → 3/(3+|stage|); stage>0 → (3+stage)/3.
    /// (Ranges from 0.333× at -6 to 3.0× at +6.)
    /// </summary>
    double GetAccuracyStageMultiplier(int stage);

    /// <summary>
    /// Converts a move's accuracy % and the combatants' accuracy/evasion stages
    /// to an internal hit threshold on the [0, AccuracyRollBound) scale.
    /// Roll Random.Next(AccuracyRollBound) and miss if roll >= threshold.
    /// </summary>
    int GetHitThreshold(int accuracyPercent, int attackerAccStage, int defenderEvaStage);

    /// <summary>Exclusive upper bound for the accuracy roll (Gen 1: 256, so a roll of 255 always misses).</summary>
    int AccuracyRollBound { get; }

    /// <summary>Whether X Accuracy skips the accuracy roll instead of raising a stage. See GENERATION_SEAMS.md §2.</summary>
    bool XAccuracyBypassesAccuracyCheck { get; }

    /// <summary>Whether a repeat Dire Hit / Guard Spec / flag-style X Accuracy is refused instead of spent. See GENERATION_SEAMS.md §2.</summary>
    bool RepeatFlagItemRefused { get; }

    /// <summary>Whether an X-item is refused instead of spent when its stat is already at +6. See GENERATION_SEAMS.md §2.</summary>
    bool CappedXItemRefused { get; }

    /// <summary>Whether Dire Hit / Guard Spec narrate their effect beyond the "used &lt;ITEM&gt;!" line. See GENERATION_SEAMS.md §2.</summary>
    bool BoosterItemAnnouncesEffect { get; }

    /// <summary>The order a move draws its crit, damage and accuracy rolls. See GENERATION_SEAMS.md §2.</summary>
    HitRollOrder HitRollOrder { get; }

    /// <summary>Whether every strike of a multi-hit move repeats the first strike's crit and damage. See GENERATION_SEAMS.md §2.</summary>
    bool MultiHitRepeatsFirstStrike { get; }

    /// <summary>Whether fixed-damage moves, Super Fang, Psywave and Counter respect a 0× type matchup. See GENERATION_SEAMS.md §2.</summary>
    bool FixedDamageChecksTypeImmunity { get; }

    /// <summary>Whether damage below 2 skips the damage roll (no draw). See GENERATION_SEAMS.md §2.</summary>
    bool DamageRollSkippedBelowTwo { get; }

    /// <summary>Whether a non-damaging move's type immunity ends it before the accuracy roll. See GENERATION_SEAMS.md §2.</summary>
    bool StatusMoveImmunityBeforeAccuracy { get; }

    /// <summary>Whether Jump Kick / Hi Jump Kick crash on a 0× target as well as on a miss. See GENERATION_SEAMS.md §2.</summary>
    bool CrashOnTypeImmunity { get; }

    /// <summary>
    /// Returns the number of turns a binding move traps the target.
    /// Gen 1: 2–5 at 3/8, 3/8, 1/8, 1/8 (GEN_DIFFERENCES.md).
    /// </summary>
    int RollBindingTurns();

    /// <summary>The crash damage a jump-kick user takes when the move misses.</summary>
    int CalculateCrashDamage(Creature user);

    /// <summary>The recoil a recoil move (Take Down, Double-Edge, Submission) deals back, given the damage it dealt.</summary>
    int CalculateRecoilDamage(int damageDealt);

    /// <summary>How many turns a rampage move (Thrash, Petal Dance) locks the user in before it confuses itself.</summary>
    int RollRampageTurns();

    /// <summary>How many turns Disable locks one of the target's moves out of selection.</summary>
    int RollDisableTurns();

    // ── Move-specific damage quirks ──────────────────────────────────────────────

    /// <summary>Whether a one-hit KO move succeeds against <paramref name="target"/>, apart from the accuracy roll.</summary>
    bool OneHitKoSucceeds(Creature user, Creature target);

    /// <summary>Whether Counter answers the last damage the user took, given that hit's type. See GENERATION_SEAMS.md §2.</summary>
    bool CounterQualifies(DamageType? lastDamageType);

    /// <summary>Divisor applied to the target's defensive stat for Self-Destruct / Explosion damage.</summary>
    int SelfDestructDefenseDivisor { get; }

    /// <summary>Attack stages a raging user gains each time a damaging move hits it.</summary>
    int RageAttackStagesPerHit { get; }

    /// <summary>Fraction of max HP a Recover-family move (Recover, Soft-Boiled) restores. See GENERATION_SEAMS.md §2.</summary>
    double RecoverHealFraction { get; }

    /// <summary>Factor on the defender's stat while the matching screen is up; crits ignore it (in the calculator).</summary>
    int ScreenDefenseMultiplier { get; }

    /// <summary>Number of turns a Bide commitment lasts (storing turns plus the release turn).</summary>
    int RollBideTurns();

    /// <summary>Multiplier on the total damage a Bide user absorbed when it unleashes.</summary>
    int BideDamageMultiplier { get; }

    /// <summary>The variable damage Psywave deals on hit (it ignores stats, type, STAB and crits), within
    /// <see cref="PsywaveDamageRange"/>. See GENERATION_SEAMS.md §2.</summary>
    int RollPsywaveDamage(Creature source, BattleSide side, IRandomSource rng);

    /// <summary>The inclusive bounds <see cref="RollPsywaveDamage"/> draws within, by the attacker's side (Gen 1: player
    /// [1, ⌊1.5L⌋−1], opponent [0, ⌊1.5L⌋−1]); the single source for the roll and the Dev Mode range.</summary>
    (int Min, int Max) PsywaveDamageRange(Creature source, BattleSide side);

    /// <summary>Whether a foe-targeting stat-down move (or a secondary drop) takes the AI handicap roll. See GENERATION_SEAMS.md §2.</summary>
    bool StatDownHandicapApplies(BattleSide side, Attack move, bool isSecondary);

    /// <summary>The AI handicap roll: true when the stat-down move fails (Gen 1: 64/256).</summary>
    bool RollStatDownHandicapFails();

    /// <summary>The fixed number of turns a Rest user sleeps, unlike the random <see cref="RollSleepTurns"/>.</summary>
    int RestSleepTurns { get; }

    // ── Type-based immunities ────────────────────────────────────────────────────

    /// <summary>Whether the target's types let it receive the status from a move of this type. See GENERATION_SEAMS.md §2.</summary>
    bool CanReceiveStatus(Creature target, StatusCondition status, DamageType moveType);

    /// <summary>Whether a non-damaging, foe-directed move consults type immunity. See GENERATION_SEAMS.md §2.</summary>
    bool PureStatusMoveChecksTypeImmunity(Attack move);

    /// <summary>Whether <paramref name="target"/> can be afflicted by Leech Seed.</summary>
    bool CanBeLeechSeeded(Creature target);

    /// <summary>Whether Roar / Whirlwind fail against a non-escapable foe. See GENERATION_SEAMS.md §2.</summary>
    bool ForceFleeFailsVsTrainer { get; }

    /// <summary>The major status a creature keeps when it leaves a battle; volatile conditions are dropped elsewhere.</summary>
    StatusCondition CarryStatusOutOfBattle(StatusCondition status);

    // ── Stat selection ─────────────────────────────────────────────────────────

    /// <summary>The attacker's offensive stat for a move of <paramref name="moveType"/>. See GENERATION_SEAMS.md §2.</summary>
    int GetOffensiveStat(Creature attacker, AttackType moveType);

    /// <summary>The defender's defensive stat for a move of <paramref name="moveType"/>. See GENERATION_SEAMS.md §2.</summary>
    int GetDefensiveStat(Creature defender, AttackType moveType);

    /// <summary>The attacker's stat stage applied to <see cref="GetOffensiveStat"/>.</summary>
    StageStat OffensiveStage(AttackType moveType);

    /// <summary>The defender's stat stage applied to <see cref="GetDefensiveStat"/>.</summary>
    StageStat DefensiveStage(AttackType moveType);

    // ── Critical hits ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the critical-hit probability (0.0–1.0) for one attack.
    /// Gen 1 normal:   floor(attacker.BaseSpeed / 2) / 256.
    /// Gen 1 high-crit: min(floor(attacker.BaseSpeed / 2) * 8, 255) / 256.
    /// Gen 1 uses BaseSpeed (unmodified by stages or status).
    /// </summary>
    double GetCritChance(Creature attacker, Attack move);

    /// <summary>Critical hit damage multiplier.</summary>
    double CritMultiplier { get; }

    /// <summary>Whether a crit bypasses all stat stages and the Burn Attack penalty.</summary>
    bool CritIgnoresStatStages { get; }

    // ── Experience ─────────────────────────────────────────────────────────────

    /// <summary>The undivided generation-rule XP a defeated enemy is worth; per-gen formula, the participant split and
    /// the separate roguelite multiplier in GENERATION_SEAMS.md → XP on faint.</summary>
    int CalculateXpAwarded(int baseExp, int enemyLevel, bool trainerOwned);

    /// <summary>Each live participant's XP share; the count can be 0. See GENERATION_SEAMS.md §2.</summary>
    int SplitXpAmongParticipants(int award, int liveParticipants);

    /// <summary>Whether a mid-turn faint skips the residuals and Hyper Beam's recharge. See GEN_DIFFERENCES.md.</summary>
    bool FaintEndsTurnImmediately { get; }
}
