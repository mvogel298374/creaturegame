using creaturegame.Combat;
using creaturegame.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Web.Battle;

public sealed class SignalRBattleEventEmitter(
    IHubContext<BattleHub, IBattleClient> hubContext,
    string connectionId) : IBattleEventEmitter
{
    public void Emit(BattleEvent evt)
    {
        var (type, payload) = MapEvent(evt);
        _ = hubContext.Clients.Client(connectionId).OnBattleEvent(type, payload);
    }

    private static (string type, object payload) MapEvent(BattleEvent evt) => evt switch
    {
        BattleStarted e    => ("BattleStarted",   new { e.PlayerName, e.EnemyName }),
        TurnStarted e      => ("TurnStarted",      new {
                                  e.TurnNumber,
                                  e.PlayerName, e.PlayerHp, e.PlayerMaxHp,
                                  PlayerStatus = e.PlayerStatus.ToString(),
                                  e.EnemyName,  e.EnemyHp,  e.EnemyMaxHp,
                                  EnemyStatus  = e.EnemyStatus.ToString(),
                                  Moves = e.PlayerMoves.Select(m => new {
                                      m.Name,
                                      Type     = m.Type.ToString(),
                                      m.PpCurrent,
                                      m.PpMax
                                  })
                              }),
        TurnEnded          => ("TurnEnded",        new { }),
        BattleEnded e      => ("BattleEnded",      new { e.WinnerName }),
        MoveUsed e         => ("MoveUsed",         new { e.AttackerName, e.MoveName }),
        MoveMissed e       => ("MoveMissed",       new { e.AttackerName, e.MoveName }),
        DamageDealt e      => ("DamageDealt",      new { e.TargetName, e.Damage, e.TypeEffectiveness, e.HpAfter, e.HpMax, e.IsCrit }),
        RecoilDamage e     => ("RecoilDamage",     new { e.SourceName, e.Damage, e.HpAfter }),
        StatusApplied e    => ("StatusApplied",    new { e.TargetName, Status = e.Status.ToString() }),
        StatusDamage e     => ("StatusDamage",     new { e.TargetName, e.Damage, Source = e.Source.ToString(), e.HpAfter }),
        StatusCleared e    => ("StatusCleared",    new { e.CreatureName, WasStatus = e.WasStatus.ToString() }),
        ActionBlocked e    => ("ActionBlocked",    new { e.CreatureName, Reason = e.Reason.ToString() }),
        ConfusionMessage e => ("ConfusionMessage", new { e.CreatureName }),
        ConfusionDamage e  => ("ConfusionDamage",  new { e.CreatureName, e.Damage, e.HpAfter }),
        ConfusionCleared e => ("ConfusionCleared", new { e.CreatureName }),
        StatStageChanged e => ("StatStageChanged", new { e.CreatureName, e.Stat, e.Delta, e.NewStage }),
        HazeClearedStages  => ("HazeClearedStages", new { }),
        DrainHealed e      => ("DrainHealed",      new { e.SourceName, e.HealAmount, e.HpAfter }),
        LeechSeedApplied e => ("LeechSeedApplied", new { e.TargetName }),
        LeechSeedDamage e  => ("LeechSeedDamage",  new { e.DrainedName, e.Damage, e.HpAfter }),
        LeechSeedHealed e  => ("LeechSeedHealed",  new { e.HealedName, e.Amount, e.HpAfter }),
        Recharging e       => ("Recharging",       new { e.CreatureName }),
        BindingStarted e   => ("BindingStarted",   new { e.TargetName, e.MoveName }),
        BindingBlocked e   => ("BindingBlocked",   new { e.CreatureName }),
        BindingDamage e    => ("BindingDamage",    new { e.TargetName, e.Damage, e.HpAfter }),
        FlinchBlocked e    => ("FlinchBlocked",    new { e.CreatureName }),
        ChargingUp e       => ("ChargingUp",       new { e.CreatureName, e.MoveName }),
        CreatureFainted e  => ("CreatureFainted",  new { e.Name }),
        LeveledUp e        => ("LeveledUp",        new { e.CreatureName, e.NewLevel }),
        _                  => ("Unknown",          new { })
    };
}