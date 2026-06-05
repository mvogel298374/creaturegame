using creaturegame.Combat;
using creaturegame.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Web.Battle;

public sealed class SignalRBattleEventEmitter(
    IHubContext<BattleHub, IBattleClient> hubContext,
    Func<string?> currentConnectionId
) : IBattleEventEmitter
{
    public void Emit(BattleEvent evt)
    {
        // Resolved per-emit so events follow the player across a reconnect (the
        // connectionId changes). If currently disconnected, the event is dropped —
        // a turn-based battle is normally blocked on player input during a gap, so
        // little is missed, and the reconnected client resumes from the next event.
        var connectionId = currentConnectionId();
        if (string.IsNullOrEmpty(connectionId))
            return;

        var (type, payload) = MapEvent(evt);
        _ = hubContext.Clients.Client(connectionId).OnBattleEvent(type, payload);
    }

    private static (string type, object payload) MapEvent(BattleEvent evt) =>
        evt switch
        {
            BattleStarted e => (
                "BattleStarted",
                new
                {
                    e.PlayerName,
                    e.EnemyName,
                    e.EnemySpeciesId,
                    e.EnemyLevel,
                }
            ),
            TurnStarted e => (
                "TurnStarted",
                new
                {
                    e.TurnNumber,
                    e.PlayerName,
                    e.PlayerHp,
                    e.PlayerMaxHp,
                    PlayerStatus = e.PlayerStatus.ToString(),
                    e.EnemyName,
                    e.EnemyHp,
                    e.EnemyMaxHp,
                    EnemyStatus = e.EnemyStatus.ToString(),
                    Moves = e.PlayerMoves.Select(m => new
                    {
                        m.Name,
                        Type = m.Type.ToString(),
                        m.PpCurrent,
                        m.PpMax,
                        m.Disabled,
                    }),
                }
            ),
            TurnEnded => ("TurnEnded", new { }),
            BattleEnded e => ("BattleEnded", new { e.WinnerName }),
            MoveUsed e => ("MoveUsed", new { e.AttackerName, e.MoveName }),
            MoveMissed e => ("MoveMissed", new { e.AttackerName, e.MoveName }),
            MoveHadNoEffect e => ("MoveHadNoEffect", new { e.TargetName, e.MoveName }),
            DamageDealt e => (
                "DamageDealt",
                new
                {
                    e.TargetName,
                    e.Damage,
                    e.TypeEffectiveness,
                    e.HpAfter,
                    e.HpMax,
                    e.IsCrit,
                }
            ),
            RecoilDamage e => (
                "RecoilDamage",
                new
                {
                    e.SourceName,
                    e.Damage,
                    e.HpAfter,
                }
            ),
            CrashDamage e => (
                "CrashDamage",
                new
                {
                    e.SourceName,
                    e.Damage,
                    e.HpAfter,
                }
            ),
            MoveDisabled e => ("MoveDisabled", new { e.TargetName, e.MoveName }),
            MoveReEnabled e => ("MoveReEnabled", new { e.CreatureName, e.MoveName }),
            MistApplied e => ("MistApplied", new { e.CreatureName }),
            StatDropBlocked e => ("StatDropBlocked", new { e.CreatureName }),
            MultiHitCompleted e => ("MultiHitCompleted", new { e.Hits }),
            CoinsScattered e => ("CoinsScattered", new { e.SourceName, e.Amount }),
            StatusApplied e => (
                "StatusApplied",
                new { e.TargetName, Status = e.Status.ToString() }
            ),
            StatusDamage e => (
                "StatusDamage",
                new
                {
                    e.TargetName,
                    e.Damage,
                    Source = e.Source.ToString(),
                    e.HpAfter,
                }
            ),
            StatusCleared e => (
                "StatusCleared",
                new { e.CreatureName, WasStatus = e.WasStatus.ToString() }
            ),
            ActionBlocked e => (
                "ActionBlocked",
                new { e.CreatureName, Reason = e.Reason.ToString() }
            ),
            ConfusionStarted e => ("ConfusionStarted", new { e.TargetName }),
            ConfusionMessage e => ("ConfusionMessage", new { e.CreatureName }),
            ConfusionDamage e => (
                "ConfusionDamage",
                new
                {
                    e.CreatureName,
                    e.Damage,
                    e.HpAfter,
                }
            ),
            ConfusionCleared e => ("ConfusionCleared", new { e.CreatureName }),
            StatStageChanged e => (
                "StatStageChanged",
                new
                {
                    e.CreatureName,
                    e.Stat,
                    e.Delta,
                    e.NewStage,
                }
            ),
            HazeClearedStages => ("HazeClearedStages", new { }),
            DrainHealed e => (
                "DrainHealed",
                new
                {
                    e.SourceName,
                    e.HealAmount,
                    e.HpAfter,
                }
            ),
            Healed e => (
                "Healed",
                new
                {
                    e.CreatureName,
                    e.HealAmount,
                    e.HpAfter,
                }
            ),
            MimicLearned e => ("MimicLearned", new { e.CreatureName, e.MoveName }),
            ScreenApplied e => ("ScreenApplied", new { e.CreatureName, e.ScreenName }),
            FocusEnergyApplied e => ("FocusEnergyApplied", new { e.CreatureName }),
            BideStoring e => ("BideStoring", new { e.CreatureName }),
            LeechSeedApplied e => ("LeechSeedApplied", new { e.TargetName }),
            LeechSeedDamage e => (
                "LeechSeedDamage",
                new
                {
                    e.DrainedName,
                    e.Damage,
                    e.HpAfter,
                }
            ),
            LeechSeedHealed e => (
                "LeechSeedHealed",
                new
                {
                    e.HealedName,
                    e.Amount,
                    e.HpAfter,
                }
            ),
            Recharging e => ("Recharging", new { e.CreatureName }),
            BindingStarted e => ("BindingStarted", new { e.TargetName, e.MoveName }),
            BindingBlocked e => ("BindingBlocked", new { e.CreatureName }),
            BindingDamage e => (
                "BindingDamage",
                new
                {
                    e.TargetName,
                    e.Damage,
                    e.HpAfter,
                }
            ),
            FlinchBlocked e => ("FlinchBlocked", new { e.CreatureName }),
            ChargingUp e => ("ChargingUp", new { e.CreatureName, e.MoveName }),
            CreatureFainted e => ("CreatureFainted", new { e.Name }),
            LeveledUp e => ("LeveledUp", new { e.CreatureName, e.NewLevel }),
            _ => ("Unknown", new { }),
        };
}
