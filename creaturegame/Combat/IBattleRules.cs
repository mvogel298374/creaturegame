using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// The kind of secondary effect a move can roll for on hit. Generations differ in how the
/// chance is stored: Gen 1 keeps a single per-move chance (so every kind reads the same
/// value), while later generations can attach independent chances per effect. Call sites
/// ask <see cref="IBattleRules.GetSecondaryEffectChance"/> by kind so they never assume the
/// Gen 1 single-column layout.
/// </summary>
public enum SecondaryEffectKind
{
    Status,
    Flinch,
    Confuse,
    StatStage,
}

/// <summary>
/// How a <i>dedicated</i> confusion move (a non-damaging status move — Confuse Ray / Supersonic) announces
/// hitting an <b>already-confused</b> target. The counter is never re-rolled in any generation (that rule is
/// gen-agnostic and lives in <c>ConfuseEffect</c>); only the message differs, so it rides the seam.
/// <para>Gen 1–2: <see cref="FailedGeneric"/> — the generic "But it failed!" (<c>ConditionalPrintButItFailed</c>
/// in pokered). Gen 3+: <see cref="AlreadyConfused"/> — names the redundancy ("… is already confused!").
/// A <i>secondary</i> confusion on a damaging move (Psybeam etc.) fails <b>silently</b> in every generation and
/// never consults this.</para>
/// </summary>
public enum RedundantConfuseAnnouncement
{
    /// <summary>The generic move-failure line ("But it failed!"). Gen 1–2.</summary>
    FailedGeneric,

    /// <summary>A message naming the redundancy ("… is already confused!"). Gen 3+.</summary>
    AlreadyConfused,
}

public interface IBattleRules
{
    /// <summary>
    /// Returns whether the given move thaws a frozen target when it hits.
    /// Gen 1: only damaging Fire-type moves that can inflict burn (not Fire Spin).
    /// Gen 2+: any Fire-type move.
    /// </summary>
    bool CanThawFrozenTarget(Attack move);

    /// <summary>
    /// Per-turn chance (0–100) for a frozen Pokémon to thaw spontaneously at the start of its turn.
    /// Gen 1: 0 (freeze is permanent until hit by the right move).
    /// Gen 2+: 20.
    /// </summary>
    int FreezeRandomThawPercent { get; }

    /// <summary>
    /// Same-Type Attack Bonus multiplier applied when a move's type matches the attacker's.
    /// Gen 1–5: 1.5. (Later mechanics like Adaptability/Terastal layer on top of this base.)
    /// </summary>
    double StabMultiplier { get; }

    /// <summary>
    /// Percent chance (0–100) that a confused creature hits itself instead of acting.
    /// Gen 1–6: 50. Gen 7+: 33.
    /// </summary>
    int ConfusionSelfHitPercent { get; }

    /// <summary>
    /// Number of times a multi-hit move (Double Slap, Comet Punch, Fury Attack…) strikes when it
    /// connects. Gen 1: 2–5 hits, weighted 2 = 3/8, 3 = 3/8, 4 = 1/8, 5 = 1/8. (Gen 5+ reweights to
    /// favour 3 more.) Drawn once per use.
    /// </summary>
    int RollMultiHitCount();

    /// <summary>
    /// Coins-per-level scattered by Pay Day. The money picked up after the battle is this value
    /// times the user's level. Gen 1: 2× level. (Later generations: 5× level.)
    /// </summary>
    int PayDayCoinMultiplier { get; }

    /// <summary>
    /// The percent chance (0–100) a move's secondary <paramref name="effect"/> applies on hit.
    /// Gen 1 stores one chance per move, so every <see cref="SecondaryEffectKind"/> reads the
    /// same column; a later generation that splits chances per effect overrides this to read
    /// the right field. Call sites stay generation-agnostic by asking here rather than reading
    /// the move's chance column directly.
    /// </summary>
    int GetSecondaryEffectChance(Attack move, SecondaryEffectKind effect);

    /// <summary>
    /// Rolls whether a move's secondary effect procs, given its <paramref name="chancePercent"/>
    /// (0–100, from <see cref="GetSecondaryEffectChance"/>). Gen 1 models this as a 1–100 roll
    /// (a simplification of the true internal n/256 scale); a later generation could roll
    /// differently. Takes the <paramref name="rng"/> explicitly so the caller's battle RNG stream
    /// stays the source of the roll (the rules' own RNG is for its self-owned rolls like
    /// <see cref="RollDamageVariance"/>). Returns true when the effect should apply.
    /// </summary>
    bool SecondaryHits(int chancePercent, IRandomSource rng);

    /// <summary>
    /// Returns the random damage multiplier for one hit.
    /// Gen 1: uniform draw from 217–255, divided by 255.
    /// Gen 2+: uniform draw from 85–100, divided by 100.
    /// </summary>
    double RollDamageVariance();

    /// <summary>
    /// Returns the number of turns the target will sleep (drawn randomly each time Sleep is applied).
    /// Gen 1: 1–7. Gen 2+: 2–5.
    /// </summary>
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
    /// Returns the recoil damage dealt to a Struggle user.
    /// Gen 1: half the damage dealt. Gen 2: half max HP. Gen 3+: quarter max HP.
    /// </summary>
    int CalculateStruggleRecoil(Creature source, int damageDealt);

    /// <summary>
    /// Divisor applied to max HP for end-of-turn Burn damage (e.g. 16 → 1/16 max HP).
    /// Gen 1–5: 16. Gen 6+: 8.
    /// </summary>
    int BurnDamageDenominator { get; }

    /// <summary>
    /// Divisor applied to max HP for end-of-turn Poison damage (e.g. 16 → 1/16 max HP).
    /// Gen 1–5: 16. Gen 6+: 8.
    /// </summary>
    int PoisonDamageDenominator { get; }

    /// <summary>
    /// Returns the fraction of max HP dealt by Bad Poison (Toxic) on the given counter tick.
    /// Gen 1: counter/16 — escalates each turn with no cap (counter starts at 1, increments after damage).
    /// </summary>
    double BadPoisonDamageFraction(int toxicCounter);

    // ── Stat stages ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the damage-stat multiplier for a stage in [-6, +6].
    /// Gen 1/2: stage≤0 → 2/(2+|stage|); stage>0 → (2+stage)/2.
    /// (Ranges from 0.25× at -6 to 4.0× at +6.)
    /// </summary>
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

    /// <summary>
    /// Exclusive upper bound for the accuracy roll.
    /// Gen 1: 256 (roll 0–255; a roll of 255 always misses — the 1/256 bug).
    /// Gen 2+: 101 (roll 0–100).
    /// </summary>
    int AccuracyRollBound { get; }

    /// <summary>
    /// Returns the number of turns a binding move traps the target.
    /// Gen 1: 2–5 turns.
    /// </summary>
    int RollBindingTurns();

    /// <summary>
    /// Returns the crash damage a jump-kick user takes when the move misses.
    /// Gen 1: a flat 1 HP (a famous quirk). Gen 2–4: based on the damage that would have been
    /// dealt; Gen 5+: half the user's max HP — those generations would read <paramref name="user"/>
    /// (and could extend the signature with the would-be damage) rather than returning a constant.
    /// </summary>
    int CalculateCrashDamage(Creature user);

    /// <summary>
    /// Returns the recoil damage a recoil move (Take Down, Double-Edge, Submission) deals back to
    /// the user, given the damage it dealt to the target.
    /// Gen 1: 1/4 of the damage dealt (minimum 1). Gen 2+: same fraction; Gen 7+ rounds differently.
    /// </summary>
    int CalculateRecoilDamage(int damageDealt);

    /// <summary>
    /// Returns how many turns a rampage move (Thrash, Petal Dance) locks the user in before it
    /// confuses itself. Gen 1: 2–3 turns. (Gen 2+: 2–3 as well; Gen 5+ reworks the confusion.)
    /// </summary>
    int RollRampageTurns();

    /// <summary>
    /// Returns how many turns Disable locks one of the target's moves out of selection.
    /// Gen 1: 1–7 turns. (Gen 3+: a fixed 4 turns.)
    /// </summary>
    int RollDisableTurns();

    // ── Move-specific damage quirks ──────────────────────────────────────────────

    /// <summary>
    /// Whether a one-hit KO move (Horn Drill, Guillotine, Fissure) succeeds against
    /// <paramref name="target"/>, independent of the normal accuracy roll.
    /// Gen 1: fails when the target's (in-battle) Speed is greater than the user's — a Speed
    /// comparison, NOT a level check. Gen 2+: fails when the user's level is below the target's.
    /// </summary>
    bool OneHitKoSucceeds(Creature user, Creature target);

    /// <summary>
    /// Whether Counter qualifies to answer the damage the user last took, given that hit's type.
    /// Counter returns double that damage when this is true.
    /// <para>
    /// Gen 1: qualifies when the last damaging move was <see cref="DamageType.Normal"/> or
    /// <see cref="DamageType.Fighting"/> — a <i>type</i> check (so Sonic Boom, Seismic Toss and Super
    /// Fang qualify, but Night Shade and Psywave don't). Gen 2+ switched to answering by the move's
    /// <i>physical</i> category instead of its type, which is why this is a seam member rather than a
    /// hardcoded type test in the engine. (A generation that keys on category will also need the engine
    /// to record the last hit's <see cref="AttackType"/>, which Gen 1 doesn't require.)
    /// </para>
    /// </summary>
    bool CounterQualifies(DamageType? lastDamageType);

    /// <summary>
    /// Divisor applied to the target's defensive stat when computing Self-Destruct / Explosion
    /// damage — the move halves the target's Defense before the calculation, making it much
    /// stronger. Gen 1–4: 2. Gen 5+: 1 (the mechanic was removed).
    /// </summary>
    int SelfDestructDefenseDivisor { get; }

    /// <summary>
    /// Number of Attack stages a Rage user gains each time it is hit by a damaging move while
    /// locked into Rage. Gen 1: 1 stage per hit (no cap beyond the normal +6). The magnitude is
    /// gen-variable in spirit (Gen 2 reworked Rage entirely), so it lives on the seam rather than
    /// inline in the attack resolver.
    /// </summary>
    int RageAttackStagesPerHit { get; }

    /// <summary>
    /// Fraction of max HP restored by a Recover-family move (Recover, Soft-Boiled). Gen 1: 1/2.
    /// (The famous Gen 1 "fails when (maxHP − HP) &amp; 255 == 255" quirk is not modelled.) Rest, which
    /// heals fully and sleeps, is a separate mechanic. On the seam so a later generation can vary it.
    /// </summary>
    double RecoverHealFraction { get; }

    /// <summary>
    /// Factor applied to the defender's defensive stat while the matching screen (Reflect → Defense,
    /// Light Screen → Special) is up. Gen 1–4 double the stat (×2); Gen 5+ instead reduce the damage
    /// directly (and screens become 5-turn/team-wide). Crits ignore screens (handled in the calculator).
    /// </summary>
    int ScreenDefenseMultiplier { get; }

    /// <summary>
    /// Number of turns a Bide commitment lasts (storing turns plus the release turn). Gen 1: 2–3
    /// (random). Gen 2: a fixed 2 turns. Gen 3+: reworked further (variable, +1 priority semantics
    /// change) — a later generation overrides this rather than reusing the Gen 1 random range.
    /// </summary>
    int RollBideTurns();

    /// <summary>
    /// Multiplier applied to the total damage a Bide user absorbed when it unleashes. Gen 1–4: 2×.
    /// Gen 5+ reworked Bide entirely (fixed 2 turns, different accumulation), so a later generation
    /// reimplements the mechanic rather than just swapping this number.
    /// </summary>
    int BideDamageMultiplier { get; }

    /// <summary>
    /// Returns the variable damage Psywave deals on hit (it ignores Attack/Defense, type
    /// effectiveness, STAB and crits). Gen 1: a random integer in [1, floor(1.5 × user level)].
    /// (Gen 2 uses a different range, Gen 3+ is 50–150% of level — the formula is gen-variable,
    /// so it lives on the seam rather than inline in the attack resolver.)
    /// </summary>
    int RollPsywaveDamage(Creature source, IRandomSource rng);

    /// <summary>
    /// The fixed number of turns a Rest user sleeps (Rest heals to full and forces sleep for exactly
    /// this many turns, unlike the random <see cref="RollSleepTurns"/>). Gen 1–2: 2 turns. The wake
    /// timing was reworked in later generations, so the duration lives on the seam.
    /// </summary>
    int RestSleepTurns { get; }

    // ── Type-based immunities ────────────────────────────────────────────────────

    /// <summary>
    /// Whether <paramref name="target"/> can receive the major <paramref name="status"/> inflicted
    /// by a move of <paramref name="moveType"/>. Generations gate status by the target's type:
    /// Gen 1 — Poison-types can't be poisoned, Fire-types can't be burned, and (a Gen 1 quirk) a
    /// Normal-type move can't paralyze a Normal-type target (Body Slam). Sleep and Freeze have no
    /// type immunity in Gen 1. Later gens add immunities (Electric→paralysis, Grass→powder, etc.),
    /// so this stays on the seam rather than being a hardcoded check in the engine.
    /// </summary>
    bool CanReceiveStatus(Creature target, StatusCondition status, DamageType moveType);

    /// <summary>
    /// Whether a <i>non-damaging</i> move consults the target's type immunity (a 0× matchup ⇒ "it doesn't
    /// affect …") — distinct from the status-vs-type check in <see cref="CanReceiveStatus"/>.
    /// <para>
    /// Gen 1: almost no status move checks it — Confuse Ray/Supersonic confuse a Ghost, Glare paralyses a
    /// Ghost, Growl lowers a Ghost's Attack, sleep/Disable land regardless of type. Only Thunder Wave
    /// (Electric ⇒ Ground is immune) and Counter (reflects damage ⇒ Ghost takes none of its Fighting type)
    /// return true. Gen 2 makes status moves respect immunity generally ⇒ seam, not a hardcoded check.
    /// Damaging moves are unaffected (they fold 0× into zero damage regardless).
    /// </para>
    /// <para>
    /// Implementations may key off the move's status/effect/type rather than a foe-direction flag — safe
    /// only because every qualifying move (Thunder Wave, Counter) is inherently foe-directed. A future move
    /// returning true here MUST be foe-directed (the result is applied against the <i>target's</i> type), or
    /// re-add a target-direction guard.
    /// </para>
    /// </summary>
    bool PureStatusMoveChecksTypeImmunity(Attack move);

    /// <summary>
    /// Whether <paramref name="target"/> can be afflicted by Leech Seed.
    /// All generations: Grass-types are immune.
    /// </summary>
    bool CanBeLeechSeeded(Creature target);

    /// <summary>
    /// Whether Roar / Whirlwind (<see cref="MoveEffect.ForceFlee"/>) have <em>no effect</em> against a
    /// trainer-analog (non-escapable) opponent. <b>Gen 1:</b> <c>true</c> — they only end a <i>wild</i>
    /// battle and simply fail in a trainer battle. <b>Gen 2+:</b> <c>false</c> — they force the opponent to
    /// switch instead, so a Gen 2 ruleset returns false and <see cref="MoveEffect.ForceFlee"/>'s effect runs
    /// its force-switch path. The wild-vs-trainer distinction itself is a run-layer fact, carried by
    /// <see cref="MoveEffectContext.BattleEscapable"/>; this seam owns the gen-variable <em>consequence</em>
    /// of facing a non-escapable foe (fail vs. force-switch).
    /// </summary>
    bool ForceFleeFailsVsTrainer { get; }

    /// <summary>
    /// The major status a creature retains when it leaves a battle — used by an endless run to carry
    /// status into the next encounter. All generations keep Sleep/Poison/Burn/Paralysis/Freeze. Gen 1
    /// reverts Bad Poison (Toxic) to regular <see cref="StatusCondition.Poison"/> out of battle (the
    /// escalating counter is volatile), whereas Gen 2+ keeps it Toxic and only resets the counter — so
    /// this is gen-variable and lives on the seam. Volatile conditions (confusion, stat stages, …) are
    /// never carried; they are dropped by the per-battle reset, not here.
    /// </summary>
    StatusCondition CarryStatusOutOfBattle(StatusCondition status);

    // ── Stat selection ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the offensive stat value used in damage calculation for the given move type.
    /// Gen 1: Physical → Attack; Special → Special (combined stat).
    /// Gen 2+: Physical → Attack; Special → SpAtk.
    /// </summary>
    int GetOffensiveStat(Creature attacker, AttackType moveType);

    /// <summary>
    /// Returns the defensive stat value used in damage calculation for the given move type.
    /// Gen 1: Physical → Defense; Special → Special (combined stat).
    /// Gen 2+: Physical → Defense; Special → SpDef.
    /// </summary>
    int GetDefensiveStat(Creature defender, AttackType moveType);

    // ── Critical hits ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the critical-hit probability (0.0–1.0) for one attack.
    /// Gen 1 normal:   floor(attacker.BaseSpeed / 2) / 256.
    /// Gen 1 high-crit: min(floor(attacker.BaseSpeed / 2) * 8, 255) / 256.
    /// Gen 1 uses BaseSpeed (unmodified by stages or status).
    /// </summary>
    double GetCritChance(Creature attacker, Attack move);

    /// <summary>
    /// Critical hit damage multiplier. Gen 1–5: 2.0. Gen 6+: 1.5.
    /// </summary>
    double CritMultiplier { get; }

    /// <summary>
    /// Whether crits bypass all stat stage modifiers and the Burn Attack penalty.
    /// True in Gen 1: crits use computed Attack/Defense/Special directly (no stages, no Burn).
    /// False in Gen 2+.
    /// </summary>
    bool CritIgnoresStatStages { get; }

    // ── Experience ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns XP awarded to the winner when an enemy faints.
    /// Gen 1 formula: floor(a × BaseExperience × EnemyLevel / 7), where <paramref name="trainerOwned"/> sets
    /// a = 1.5 (the trainer bonus, which has existed since Gen 1) vs a = 1 for a wild foe.
    /// Gen 5+: additionally divides the gain by the number of participants.
    /// </summary>
    int CalculateXpAwarded(int baseExp, int enemyLevel, bool trainerOwned);
}
