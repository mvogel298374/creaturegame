using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public abstract record BattleEvent;

// --- Battle lifecycle ---
public record BattleStarted(string PlayerName, string EnemyName, int EnemySpeciesId, int EnemyLevel)
    : BattleEvent;

public record TurnStarted(
    int TurnNumber,
    string PlayerName,
    int PlayerHp,
    int PlayerMaxHp,
    StatusCondition PlayerStatus,
    int PlayerXpThisLevel,
    int PlayerXpToNextLevel,
    string EnemyName,
    int EnemyHp,
    int EnemyMaxHp,
    StatusCondition EnemyStatus,
    IReadOnlyList<MoveInfo> PlayerMoves
) : BattleEvent;

public record MoveInfo(
    string Name,
    DamageType Type,
    int PpCurrent,
    int PpMax,
    bool Disabled = false,
    // True when this move gets the Same-Type Attack Bonus for its user: a damaging move whose type matches
    // one of the user's current types. Computed against the creature's live type (so it stays correct after
    // Conversion/Transform), letting the UI flag STAB moves in the menu without re-deriving the rule client-side.
    bool Stab = false,
    // The move's type-effectiveness multiplier vs the *current* opponent (product over the opponent's types):
    // one of 0, ¼, ½, 1, 2, 4 in Gen 1. Computed for damaging moves only via the active ITypeChart (so it
    // reflects the live matchup incl. enemy Conversion/Transform and chart quirks); non-damaging/fixed-damage
    // moves report 1.0 (neutral = "no cue"). Lets the menu show a ×N effectiveness pill without the UI knowing
    // the type chart.
    double Effectiveness = 1.0
);

public record TurnEnded : BattleEvent;

public record BattleEnded(string WinnerName) : BattleEvent;

/// <summary>The endless battle chain ended — the player's creature fainted. Carries the run summary for
/// the game-over screen. Emitted once, after the final <see cref="BattleEnded"/>.</summary>
public record RunEnded(int BattlesWon, int FinalLevel, string FinalCreatureName) : BattleEvent;

/// <summary>A roguelite "Poké Center" recovery is offered between encounters (after a set number of wins).
/// A blocking event: the run loop awaits the player's accept/skip decision via
/// <see cref="IBattleInput.ConfirmRecoveryAsync"/> before continuing, so the client raises the heal modal here.
/// Carries the species id so the modal can show the creature's sprite.</summary>
public record RecoveryOffered(string CreatureName, int SpeciesId, int BattlesWon) : BattleEvent;

/// <summary>The player accepted a Poké Center recovery and the creature was fully restored (HP, PP, and
/// status). Carries the post-heal HP so the client can fill the bar. Emitted between encounters, never inside
/// a battle.</summary>
public record PlayerRecovered(string CreatureName, int HpAfter) : BattleEvent;

/// <summary>The player declined the offered Poké Center recovery (kept current HP/PP/status). Drives the
/// "decided to keep going" line.</summary>
public record RecoveryDeclined(string CreatureName) : BattleEvent;

// --- Move actions ---
public record MoveUsed(string AttackerName, string MoveName) : BattleEvent;

public record MoveMissed(string AttackerName, string MoveName) : BattleEvent;

/// <summary>The move hit but the target is immune (type-based) so nothing happened — "It doesn't affect …".</summary>
public record MoveHadNoEffect(string TargetName, string MoveName) : BattleEvent;

/// <summary>A move with no effect by design (Splash) resolved — the Gen 1 "But nothing happened!" line.</summary>
public record ButNothingHappened(string CreatureName) : BattleEvent;

/// <summary>The user spent HP to put up a Substitute decoy with <paramref name="SubstituteHp"/> HP.</summary>
public record SubstitutePutUp(string CreatureName, int SubstituteHp) : BattleEvent;

/// <summary>An incoming hit was soaked by the creature's Substitute (the user took no damage).</summary>
public record SubstituteAbsorbedHit(string CreatureName, int SubstituteHpAfter) : BattleEvent;

/// <summary>The Substitute ran out of HP and broke — the user is exposed again.</summary>
public record SubstituteFaded(string CreatureName) : BattleEvent;

public record DamageDealt(
    string TargetName,
    int Damage,
    double TypeEffectiveness,
    int HpAfter,
    int HpMax,
    bool IsCrit = false
) : BattleEvent;

public record RecoilDamage(string SourceName, int Damage, int HpAfter) : BattleEvent;

public record MultiHitCompleted(int Hits) : BattleEvent;

public record CoinsScattered(string SourceName, int Amount) : BattleEvent;

// --- Status conditions ---
public record StatusApplied(string TargetName, StatusCondition Status) : BattleEvent;

public record StatusDamage(string TargetName, int Damage, StatusCondition Source, int HpAfter)
    : BattleEvent;

public record StatusCleared(string CreatureName, StatusCondition WasStatus) : BattleEvent;

public record ActionBlocked(string CreatureName, StatusCondition Reason) : BattleEvent;

// --- Confusion (pseudo-status — separate from StatusCondition enum) ---
public record ConfusionStarted(string TargetName) : BattleEvent;

public record ConfusionMessage(string CreatureName) : BattleEvent;

public record ConfusionDamage(string CreatureName, int Damage, int HpAfter) : BattleEvent;

public record ConfusionCleared(string CreatureName) : BattleEvent;

// --- Stat stages ---
public record StatStageChanged(string CreatureName, string Stat, int Delta, int NewStage)
    : BattleEvent;

public record HazeClearedStages : BattleEvent;

// --- Drain / healing ---
public record DrainHealed(string SourceName, int HealAmount, int HpAfter) : BattleEvent;

/// <summary>A self-heal move (Recover, Soft-Boiled) restored HP to the user.</summary>
public record Healed(string CreatureName, int HealAmount, int HpAfter) : BattleEvent;

// --- Mimic (the move) ---
public record MimicLearned(string CreatureName, string MoveName) : BattleEvent;

// --- Transform / Conversion (identity & type mutation) ---
/// <summary>The user copied the target's species, types, stats and moveset (Transform). <paramref name="IntoSpeciesId"/>
/// is the species the user became — the client morphs the transforming side's sprite to it.</summary>
public record TransformedInto(string CreatureName, string TargetName, int IntoSpeciesId)
    : BattleEvent;

/// <summary>The user changed its type — Gen 1 Conversion copies the foe's primary type.</summary>
public record ConvertedType(string CreatureName, DamageType NewType) : BattleEvent;

// --- Reflect / Light Screen ---
public record ScreenApplied(string CreatureName, string ScreenName) : BattleEvent;

// --- Focus Energy ---
public record FocusEnergyApplied(string CreatureName) : BattleEvent;

// --- Bide ---
public record BideStoring(string CreatureName) : BattleEvent;

// --- Leech Seed ---
public record LeechSeedApplied(string TargetName) : BattleEvent;

public record LeechSeedDamage(string DrainedName, int Damage, int HpAfter) : BattleEvent;

public record LeechSeedHealed(string HealedName, int Amount, int HpAfter) : BattleEvent;

// --- Recharge (Hyper Beam) ---
public record Recharging(string CreatureName) : BattleEvent;

// --- Binding (Wrap, Bind, Clamp, Fire Spin) ---
public record BindingStarted(string TargetName, string MoveName) : BattleEvent;

public record BindingBlocked(string CreatureName) : BattleEvent;

// --- Flinch ---
public record FlinchBlocked(string CreatureName) : BattleEvent;

// --- Two-turn moves (Fly, Dig, SolarBeam…) ---
public record ChargingUp(string CreatureName, string MoveName) : BattleEvent;

// --- Crash damage (Jump Kick / Hi Jump Kick miss) ---
public record CrashDamage(string SourceName, int Damage, int HpAfter) : BattleEvent;

// --- Disable (the move) ---
public record MoveDisabled(string TargetName, string MoveName) : BattleEvent;

public record MoveReEnabled(string CreatureName, string MoveName) : BattleEvent;

// --- Mist (the move) ---
public record MistApplied(string CreatureName) : BattleEvent;

public record StatDropBlocked(string CreatureName) : BattleEvent;

// --- Creature ---
public record CreatureFainted(string Name) : BattleEvent;

/// <summary>The amount of XP a creature earned from a win — emitted once, before any <see cref="LeveledUp"/>
/// events, so the client can show the gain and begin filling the XP bar.</summary>
public record ExperienceGained(string CreatureName, int Amount) : BattleEvent;

/// <summary>One level gained. Carries the new level's bar parameters (<paramref name="XpThisLevel"/> /
/// <paramref name="XpToNextLevel"/>), the resulting stat totals (<paramref name="Stats"/>) and the per-stat
/// gains from this level (<paramref name="StatGains"/>) so the client can refill the bar and show the Gen 1
/// level-up stat panel. A multi-level award emits one of these per level, in order.</summary>
public record LeveledUp(
    string CreatureName,
    int NewLevel,
    int XpThisLevel,
    int XpToNextLevel,
    StatBlock Stats,
    StatBlock StatGains
) : BattleEvent;

/// <summary>The player's creature evolved between encounters. Carries both the old and new names plus the
/// <paramref name="FromSpeciesId"/>/<paramref name="ToSpeciesId"/> so the client can morph the sprite
/// (old → silhouette → new) — the same id-driven approach as <see cref="TransformedInto"/>. Emitted in the
/// run loop after a win's level-ups resolve, before any evolution move-learning. Followed by the evolved
/// form's <see cref="MoveLearned"/> events, if any.</summary>
public record CreatureEvolved(string FromName, string ToName, int FromSpeciesId, int ToSpeciesId)
    : BattleEvent;

// --- Learnset (level-up move learning) ---
/// <summary>The creature learned a new move — either into a free slot, or after a replacement. Drives the
/// canonical "{NAME} learned {MOVE}!" line.</summary>
public record MoveLearned(string CreatureName, string MoveName) : BattleEvent;

/// <summary>The creature levelled into a move but its four slots are full, so the player must choose a move to
/// forget (or decline). A blocking event: the battle loop awaits the player's decision via
/// <see cref="IBattleInput.ChooseMoveToForgetAsync"/> before continuing. Carries the current move names so the
/// UI can present the choice.</summary>
public record MoveReplacementRequired(
    string CreatureName,
    string NewMoveName,
    IReadOnlyList<string> CurrentMoves
) : BattleEvent;

/// <summary>A move was forgotten to make room for a new one — emitted just before the paired
/// <see cref="MoveLearned"/>. Drives the "{NAME} forgot {MOVE}!" line.</summary>
public record MoveForgotten(string CreatureName, string MoveName) : BattleEvent;

/// <summary>The player declined to learn the offered move (kept the current four). Drives the
/// "{NAME} did not learn {MOVE}." line.</summary>
public record MoveLearnDeclined(string CreatureName, string MoveName) : BattleEvent;
