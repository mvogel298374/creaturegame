using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public abstract record BattleEvent;

// --- Battle lifecycle ---
public record BattleStarted(string PlayerName, string EnemyName, int EnemySpeciesId, int EnemyLevel) : BattleEvent;

public record TurnStarted(
    int TurnNumber,
    string PlayerName, int PlayerHp, int PlayerMaxHp, StatusCondition PlayerStatus,
    string EnemyName,  int EnemyHp,  int EnemyMaxHp,  StatusCondition EnemyStatus,
    IReadOnlyList<MoveInfo> PlayerMoves
) : BattleEvent;

public record MoveInfo(string Name, DamageType Type, int PpCurrent, int PpMax, bool Disabled = false);

public record TurnEnded : BattleEvent;
public record BattleEnded(string WinnerName) : BattleEvent;

// --- Move actions ---
public record MoveUsed(string AttackerName, string MoveName) : BattleEvent;
public record MoveMissed(string AttackerName, string MoveName) : BattleEvent;
public record DamageDealt(string TargetName, int Damage, double TypeEffectiveness, int HpAfter, int HpMax, bool IsCrit = false) : BattleEvent;
public record RecoilDamage(string SourceName, int Damage, int HpAfter) : BattleEvent;
public record MultiHitCompleted(int Hits) : BattleEvent;
public record CoinsScattered(string SourceName, int Amount) : BattleEvent;

// --- Status conditions ---
public record StatusApplied(string TargetName, StatusCondition Status) : BattleEvent;
public record StatusDamage(string TargetName, int Damage, StatusCondition Source, int HpAfter) : BattleEvent;
public record StatusCleared(string CreatureName, StatusCondition WasStatus) : BattleEvent;
public record ActionBlocked(string CreatureName, StatusCondition Reason) : BattleEvent;

// --- Confusion (pseudo-status — separate from StatusCondition enum) ---
public record ConfusionStarted(string TargetName) : BattleEvent;
public record ConfusionMessage(string CreatureName) : BattleEvent;
public record ConfusionDamage(string CreatureName, int Damage, int HpAfter) : BattleEvent;
public record ConfusionCleared(string CreatureName) : BattleEvent;

// --- Stat stages ---
public record StatStageChanged(string CreatureName, string Stat, int Delta, int NewStage) : BattleEvent;
public record HazeClearedStages : BattleEvent;

// --- Drain / healing ---
public record DrainHealed(string SourceName, int HealAmount, int HpAfter) : BattleEvent;

// --- Leech Seed ---
public record LeechSeedApplied(string TargetName) : BattleEvent;
public record LeechSeedDamage(string DrainedName, int Damage, int HpAfter) : BattleEvent;
public record LeechSeedHealed(string HealedName, int Amount, int HpAfter) : BattleEvent;

// --- Recharge (Hyper Beam) ---
public record Recharging(string CreatureName) : BattleEvent;

// --- Binding (Wrap, Bind, Clamp, Fire Spin) ---
public record BindingStarted(string TargetName, string MoveName) : BattleEvent;
public record BindingBlocked(string CreatureName) : BattleEvent;
public record BindingDamage(string TargetName, int Damage, int HpAfter) : BattleEvent;

// --- Flinch ---
public record FlinchBlocked(string CreatureName) : BattleEvent;

// --- Two-turn moves (Fly, Dig, SolarBeam…) ---
public record ChargingUp(string CreatureName, string MoveName) : BattleEvent;

// --- Crash damage (Jump Kick / Hi Jump Kick miss) ---
public record CrashDamage(string SourceName, int Damage, int HpAfter) : BattleEvent;

// --- Disable (the move) ---
public record MoveDisabled(string TargetName, string MoveName) : BattleEvent;
public record MoveReEnabled(string CreatureName, string MoveName) : BattleEvent;

// --- Creature ---
public record CreatureFainted(string Name) : BattleEvent;
public record LeveledUp(string CreatureName, int NewLevel) : BattleEvent;
