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

/// <summary>Which line a move that fails on a precondition prints. See GENERATION_SEAMS.md §2.</summary>
public enum MoveFailureAnnouncement
{
    /// <summary>"&lt;USER&gt;'s &lt;MOVE&gt; missed!" (<see cref="MoveMissed"/>).</summary>
    Missed,

    /// <summary><see cref="MoveHadNoEffect"/>; its lines are in GENERATION_SEAMS.md §2 (Failure announcements).</summary>
    DoesntAffect,

    /// <summary>"&lt;TARGET&gt; is unaffected!" (<see cref="MoveUnaffected"/>).</summary>
    Unaffected,

    /// <summary>"It didn't affect &lt;TARGET&gt;!" (<see cref="MoveDidntAffect"/>).</summary>
    DidntAffect,

    /// <summary>"But it failed!" (<see cref="MoveFailed"/>).</summary>
    Failed,
}

/// <summary>How a rampage move (Thrash, Petal Dance) announces a locked repeat turn. See GENERATION_SEAMS.md §2.</summary>
public enum RampageRepeatAnnouncement
{
    /// <summary>"&lt;USER&gt; used &lt;MOVE&gt;!" (<see cref="MoveUsed"/>), as on the first turn.</summary>
    UsedMove,

    /// <summary>"&lt;USER&gt; is thrashing about!" (<see cref="RampageContinues"/>) instead of the "used" line. Gen 1.</summary>
    ThrashingAbout,
}

/// <summary>The lines a hit that breaks a Substitute prints. See GENERATION_SEAMS.md §2.</summary>
public enum SubstituteBreakAnnouncement
{
    /// <summary><see cref="SubstituteAbsorbedHit"/>, then "&lt;TARGET&gt;'s SUBSTITUTE broke!" (<see cref="SubstituteBroke"/>). Gen 1.</summary>
    TookDamageThenBroke,

    /// <summary><see cref="SubstituteAbsorbedHit"/>, then "&lt;TARGET&gt;'s SUBSTITUTE faded!" (<see cref="SubstituteFaded"/>).</summary>
    TookDamageThenFaded,
}

/// <summary>The line a successful Substitute prints. See GENERATION_SEAMS.md §2.</summary>
public enum SubstituteSetupAnnouncement
{
    /// <summary>"It created a SUBSTITUTE!" (<see cref="SubstituteCreated"/>). Gen 1.</summary>
    ItCreated,

    /// <summary>"&lt;USER&gt; made a SUBSTITUTE!" (<see cref="SubstituteMade"/>).</summary>
    UserMade,
}

/// <summary>The line a Substitute that can't be raised prints. See GENERATION_SEAMS.md §2.</summary>
public enum SubstituteFailureAnnouncement
{
    /// <summary>"&lt;USER&gt; has a SUBSTITUTE!" or "Too weak to make a SUBSTITUTE!". Gen 1.</summary>
    NamedReason,

    /// <summary>"But it failed!" (<see cref="MoveFailed"/>).</summary>
    Failed,
}

/// <summary>One step of the routine a creature runs before it moves. See GENERATION_SEAMS.md §2.</summary>
public enum PreMoveCheck
{
    Sleep,
    Freeze,
    HeldByTrap,
    Flinch,
    Recharge,
    DisableCountdown,
    Confusion,
    UsingDisabledMove,
    Paralysis,
}

/// <summary>What can clear a pending recharge when it lands. See GENERATION_SEAMS.md §2.</summary>
public enum RechargeClearingInfliction
{
    Sleep,
    Freeze,
    Flinch,
    Trap,
}

/// <summary>The line printed when Mist stops a stat drop. See GENERATION_SEAMS.md §2.</summary>
public enum MistBlockAnnouncement
{
    /// <summary>"But it failed!" (<see cref="MoveFailed"/>). Gen 1.</summary>
    Failed,

    /// <summary>"&lt;TARGET&gt; is protected by the mist!" (<see cref="StatDropBlocked"/>).</summary>
    ProtectedByMist,
}

/// <summary>The wording of the crit, one-hit KO and effectiveness lines. See GENERATION_SEAMS.md §2.</summary>
public enum HitLineWording
{
    /// <summary>"Critical hit!", "One-hit KO!", "It's super effective!", "It's not very effective...". Gen 1.</summary>
    Short,

    /// <summary>"A critical hit!", "It's a one-hit KO!", "It's super-effective!", "It's not very effective…".</summary>
    Long,
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

    /// <summary>The power of a confusion self-hit, a typeless physical hit. See GENERATION_SEAMS.md §2.</summary>
    int ConfusionSelfHitPower { get; }

    /// <summary>Whether a confusion self-hit rolls damage variance. See GENERATION_SEAMS.md §2.</summary>
    bool ConfusionSelfHitRollsVariance { get; }

    /// <summary>Whether the foe's Reflect, not the user's, doubles a confusion self-hit's Defense. See GENERATION_SEAMS.md §2.</summary>
    bool ConfusionSelfHitUsesFoeReflect { get; }

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

    /// <summary>Whether a self-hit or crash by a Substitute holder lands on the foe's Substitute. See GENERATION_SEAMS.md §2.</summary>
    bool SelfInflictedDamageStrikesFoeSubstitute { get; }

    /// <summary>The damage an OHKO deals a standing Substitute. See GENERATION_SEAMS.md §2.</summary>
    int OhkoDamageToSubstitute { get; }

    /// <summary>The lines a hit that breaks a Substitute prints. See GENERATION_SEAMS.md §2.</summary>
    SubstituteBreakAnnouncement SubstituteBreakAnnouncement { get; }

    /// <summary>The line a successful Substitute prints. See GENERATION_SEAMS.md §2.</summary>
    SubstituteSetupAnnouncement SubstituteSetupAnnouncement { get; }

    /// <summary>The line a Substitute that can't be raised prints. See GENERATION_SEAMS.md §2.</summary>
    SubstituteFailureAnnouncement SubstituteFailureAnnouncement { get; }

    /// <summary>Whether a hit soaked by a Substitute still prints its crit, one-hit KO and effectiveness lines. See GENERATION_SEAMS.md §2.</summary>
    bool SoakedHitPrintsCritAndEffectiveness { get; }

    /// <summary>Whether a multi-hit move prints its effectiveness line on every strike or only the first. See GENERATION_SEAMS.md §2.</summary>
    bool EffectivenessLineRepeatsOnEveryStrike { get; }

    /// <summary>Whether a damageless move's Substitute, status and Mist checks run before its accuracy roll. See GENERATION_SEAMS.md §2.</summary>
    bool PrimaryEffectChecksPrecedeHitTest { get; }

    /// <summary>Whether a pure status move can affect this target, by type. See GENERATION_SEAMS.md §2.</summary>
    bool PureStatusMoveCanAffect(Creature target, StatusCondition status);

    /// <summary>Whether a Substitute stops this pure status move. See GENERATION_SEAMS.md §2.</summary>
    bool SubstituteStopsPureStatusMove(Attack move);

    /// <summary>Whether Mist stops a primary or secondary stat drop. See GENERATION_SEAMS.md §2.</summary>
    bool MistStopsStatDrop(bool isSecondary);

    /// <summary>The line printed when Mist stops a stat drop. See GENERATION_SEAMS.md §2.</summary>
    MistBlockAnnouncement MistBlockAnnouncement { get; }

    /// <summary>Whether a Substitute stops a hit from thawing the frozen target behind it. See GENERATION_SEAMS.md §2.</summary>
    bool SubstituteStopsThaw { get; }

    /// <summary>The line a stat-down or confusion move prints when a Substitute stops it or it misses. See GENERATION_SEAMS.md §2.</summary>
    MoveFailureAnnouncement PrimaryEffectFailureAnnouncement { get; }

    /// <summary>The checks a creature runs before it moves, in order. See GENERATION_SEAMS.md §2.</summary>
    IReadOnlyList<PreMoveCheck> PreMoveCheckOrder { get; }

    /// <summary>Whether losing the turn to this check ends the user's Bide, rampage, charge and trap. See GENERATION_SEAMS.md §2.</summary>
    bool PreMoveCheckCancelsLockIn(PreMoveCheck check);

    /// <summary>Whether being stopped from using a disabled move ends the user's charge. See GENERATION_SEAMS.md §2.</summary>
    bool DisabledMoveBlockEndsCharge { get; }

    /// <summary>Whether this infliction clears the target's pending recharge. See GENERATION_SEAMS.md §2.</summary>
    bool InflictionClearsRecharge(RechargeClearingInfliction infliction);

    /// <summary>Whether a sleep move on a recharging target skips its checks and always lands. See GENERATION_SEAMS.md §2.</summary>
    bool SleepMoveIgnoresChecksOnRechargingTarget { get; }

    /// <summary>Whether a sleep or frozen turn erases the move Mirror Move would copy. See GENERATION_SEAMS.md §2.</summary>
    bool SleepOrFreezeTurnForgetsLastMove { get; }

    /// <summary>Rolls whether a paralyzed creature loses this turn. See GENERATION_SEAMS.md §2.</summary>
    bool RollsFullParalysis(IRandomSource rng);

    /// <summary>Whether replacing a binder frees the creature it held. See GENERATION_SEAMS.md §2.</summary>
    bool ReplacementEndsTrap { get; }

    /// <summary>The wording of the crit, one-hit KO and effectiveness lines. See GENERATION_SEAMS.md §2.</summary>
    HitLineWording HitLineWording { get; }

    /// <summary>The byte a strike leaves and whether it broke. See GENERATION_SEAMS.md §2.</summary>
    SubstituteStrike StrikeSubstituteByte(int remnant, int damage);

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

    /// <summary>How a rampage move announces each turn after the first. See GENERATION_SEAMS.md §2.</summary>
    RampageRepeatAnnouncement RampageRepeatAnnouncement { get; }

    /// <summary>Whether the confusion a rampage's end inflicts prints "became confused!". See GENERATION_SEAMS.md §2.</summary>
    bool RampageEndAnnouncesConfusion { get; }

    /// <summary>Whether a rampage's end rolls a fresh confusion counter over a running one. See GENERATION_SEAMS.md §2.</summary>
    bool RampageEndOverwritesConfusion { get; }

    /// <summary>How many turns Disable locks one of the target's moves out of selection.</summary>
    int RollDisableTurns();

    // ── Move-specific damage quirks ──────────────────────────────────────────────

    /// <summary>Whether a one-hit KO move succeeds against <paramref name="target"/>, apart from the accuracy roll.</summary>
    bool OneHitKoSucceeds(Creature user, Creature target);

    /// <summary>The line a one-hit KO move prints when <see cref="OneHitKoSucceeds"/> is false. See GENERATION_SEAMS.md §2.</summary>
    MoveFailureAnnouncement OneHitKoFailureAnnouncement { get; }

    /// <summary>Whether Counter answers the last damage the user took, given that hit's type. See GENERATION_SEAMS.md §2.</summary>
    bool CounterQualifies(DamageType? lastDamageType);

    /// <summary>Divisor applied to the target's defensive stat for Self-Destruct / Explosion damage.</summary>
    int SelfDestructDefenseDivisor { get; }

    /// <summary>Attack stages a raging user gains each time a damaging move hits it.</summary>
    int RageAttackStagesPerHit { get; }

    /// <summary>Fraction of max HP a Recover-family move (Recover, Soft-Boiled) restores. See GENERATION_SEAMS.md §2.</summary>
    double RecoverHealFraction { get; }

    /// <summary>Whether Recover, Soft-Boiled or Rest fails on the user's HP. See GENERATION_SEAMS.md §2.</summary>
    bool HealFails(int currentHp, int maxHp);

    /// <summary>The line a failed Recover, Soft-Boiled or Rest prints. See GENERATION_SEAMS.md §2.</summary>
    MoveFailureAnnouncement HealFailureAnnouncement { get; }

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

    /// <summary>The line Roar / Whirlwind print when they fail against a non-escapable foe. See GENERATION_SEAMS.md §2.</summary>
    MoveFailureAnnouncement ForceFleeTrainerFailureAnnouncement { get; }

    /// <summary>Whether Roar / Whirlwind succeed in a wild battle, given both levels. See GENERATION_SEAMS.md §2.</summary>
    bool ForceFleeSucceeds(int userLevel, int targetLevel, IRandomSource rng);

    /// <summary>The line Roar / Whirlwind print when the wild success roll fails. See GENERATION_SEAMS.md §2.</summary>
    MoveFailureAnnouncement ForceFleeWildFailureAnnouncement { get; }

    /// <summary>Whether Roar / Whirlwind skip the accuracy roll. See GENERATION_SEAMS.md §2.</summary>
    bool ForceFleeSkipsAccuracy { get; }

    /// <summary>Whether a flee ends the turn at once: no further action, counter tick or residual. See GENERATION_SEAMS.md §2.</summary>
    bool FleeEndsTurnImmediately { get; }

    /// <summary>The line a pure status move prints on an already-statused or status-immune foe. See GENERATION_SEAMS.md §2.</summary>
    MoveFailureAnnouncement StatusMoveNoOpAnnouncement { get; }

    /// <summary>The line a pure status move that inflicts a major status prints when it misses. See GENERATION_SEAMS.md §2.</summary>
    MoveFailureAnnouncement StatusMoveMissAnnouncement { get; }

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

    /// <summary>The XP and Stat Exp terms <see cref="ExperienceFormula"/> reads. See GENERATION_SEAMS.md → XP on faint.</summary>
    ExperienceRules Experience { get; }

    /// <summary>Whether a mid-turn faint skips the residuals and Hyper Beam's recharge. See GEN_DIFFERENCES.md.</summary>
    bool FaintEndsTurnImmediately { get; }
}
