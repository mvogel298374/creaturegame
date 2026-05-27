using creaturegame.Attacks;
using creaturegame.Creatures;

namespace creaturegame.Combat;

public abstract record BattleEvent;

// --- Battle lifecycle ---
public record BattleStarted(string PlayerName, string EnemyName) : BattleEvent;

public record TurnStarted(
    int TurnNumber,
    string PlayerName, int PlayerHp, int PlayerMaxHp, StatusCondition PlayerStatus,
    string EnemyName,  int EnemyHp,  int EnemyMaxHp,  StatusCondition EnemyStatus,
    IReadOnlyList<MoveInfo> PlayerMoves
) : BattleEvent;

public record MoveInfo(string Name, DamageType Type, int PpCurrent, int PpMax);

public record TurnEnded : BattleEvent;
public record BattleEnded(string WinnerName) : BattleEvent;

// --- Move actions ---
public record MoveUsed(string AttackerName, string MoveName) : BattleEvent;
public record MoveMissed(string AttackerName, string MoveName) : BattleEvent;
public record DamageDealt(string TargetName, int Damage, double TypeEffectiveness, int HpAfter, int HpMax, bool IsCrit = false) : BattleEvent;
public record RecoilDamage(string SourceName, int Damage, int HpAfter) : BattleEvent;

// --- Status conditions ---
public record StatusApplied(string TargetName, StatusCondition Status) : BattleEvent;
public record StatusDamage(string TargetName, int Damage, StatusCondition Source, int HpAfter) : BattleEvent;
public record StatusCleared(string CreatureName, StatusCondition WasStatus) : BattleEvent;
public record ActionBlocked(string CreatureName, StatusCondition Reason) : BattleEvent;

// --- Confusion (pseudo-status — separate from StatusCondition enum) ---
public record ConfusionMessage(string CreatureName) : BattleEvent;
public record ConfusionDamage(string CreatureName, int Damage, int HpAfter) : BattleEvent;
public record ConfusionCleared(string CreatureName) : BattleEvent;

// --- Stat stages ---
public record StatStageChanged(string CreatureName, string Stat, int Delta, int NewStage) : BattleEvent;
public record HazeClearedStages : BattleEvent;

// --- Creature ---
public record CreatureFainted(string Name) : BattleEvent;
