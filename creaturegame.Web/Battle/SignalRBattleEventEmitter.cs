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

    // internal for the web event-contract test (reflection-checks every BattleEvent maps to a
    // non-"Unknown" type); not part of the public API.
    internal static (string type, object payload) MapEvent(BattleEvent evt) =>
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
                    e.PlayerXpThisLevel,
                    e.PlayerXpToNextLevel,
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
                        m.Stab,
                        m.Effectiveness,
                    }),
                }
            ),
            TurnEnded => ("TurnEnded", new { }),
            BattleEnded e => ("BattleEnded", new { e.WinnerName }),
            RunEnded e => (
                "RunEnded",
                new
                {
                    e.BattlesWon,
                    e.FinalLevel,
                    e.FinalCreatureName,
                }
            ),
            RecoveryOffered e => (
                "RecoveryOffered",
                new
                {
                    e.CreatureName,
                    e.SpeciesId,
                    e.BattlesWon,
                }
            ),
            PlayerRecovered e => ("PlayerRecovered", new { e.CreatureName, e.HpAfter }),
            RecoveryDeclined e => ("RecoveryDeclined", new { e.CreatureName }),
            BiomeChoiceOffered e => (
                "BiomeChoiceOffered",
                new
                {
                    Options = e.Options.Select(o => new
                    {
                        o.Id,
                        o.Name,
                        Types = o.Types.Select(t => t.ToString()),
                    }),
                }
            ),
            BiomeEntered e => (
                "BiomeEntered",
                new
                {
                    e.BiomeId,
                    e.BiomeName,
                    Types = e.Types.Select(t => t.ToString()),
                }
            ),
            RunNodeEntered e => ("RunNodeEntered", new { e.Kind }),
            RewardGranted e => (
                "RewardGranted",
                new
                {
                    e.Source,
                    e.Gold,
                    e.GoldTotal,
                    e.ItemNames,
                }
            ),
            RewardChoiceOffered e => (
                "RewardChoiceOffered",
                new { e.Source, Options = e.Options.Select(ProjectRewardOption) }
            ),
            ShopOffered e => (
                "ShopOffered",
                new { Items = e.Items.Select(ProjectShopItem), e.Balance }
            ),
            ShopItemPurchased e => (
                "ShopItemPurchased",
                new
                {
                    e.ItemName,
                    e.Price,
                    e.Balance,
                }
            ),
            ItemUsed e => ("ItemUsed", new { e.ItemName, e.TargetName }),
            PpRestored e => (
                "PpRestored",
                new
                {
                    e.CreatureName,
                    e.MoveName,
                    e.PpAfter,
                }
            ),
            ItemUseFailed e => ("ItemUseFailed", new { e.ItemName }),
            MoveUsed e => ("MoveUsed", new { e.AttackerName, e.MoveName }),
            MoveMissed e => ("MoveMissed", new { e.AttackerName, e.MoveName }),
            MoveHadNoEffect e => ("MoveHadNoEffect", new { e.TargetName, e.MoveName }),
            ButNothingHappened e => ("ButNothingHappened", new { e.CreatureName }),
            SubstitutePutUp e => ("SubstitutePutUp", new { e.CreatureName, e.SubstituteHp }),
            SubstituteAbsorbedHit e => (
                "SubstituteAbsorbedHit",
                new { e.CreatureName, e.SubstituteHpAfter }
            ),
            SubstituteFaded e => ("SubstituteFaded", new { e.CreatureName }),
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
            TransformedInto e => (
                "TransformedInto",
                new
                {
                    e.CreatureName,
                    e.TargetName,
                    e.IntoSpeciesId,
                }
            ),
            ConvertedType e => (
                "ConvertedType",
                new { e.CreatureName, NewType = e.NewType.ToString() }
            ),
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
            FlinchBlocked e => ("FlinchBlocked", new { e.CreatureName }),
            ChargingUp e => ("ChargingUp", new { e.CreatureName, e.MoveName }),
            CreatureFainted e => ("CreatureFainted", new { e.Name }),
            CreatureFled e => ("CreatureFled", new { e.Name, e.IsPlayer }),
            ExperienceGained e => ("ExperienceGained", new { e.CreatureName, e.Amount }),
            LeveledUp e => (
                "LeveledUp",
                new
                {
                    e.CreatureName,
                    e.NewLevel,
                    e.XpThisLevel,
                    e.XpToNextLevel,
                    e.Stats,
                    e.StatGains,
                }
            ),
            EvolutionOffered e => (
                "EvolutionOffered",
                new
                {
                    e.FromName,
                    e.ToName,
                    e.FromSpeciesId,
                    e.ToSpeciesId,
                }
            ),
            EvolutionCancelled e => ("EvolutionCancelled", new { e.CreatureName }),
            CreatureEvolved e => (
                "CreatureEvolved",
                new
                {
                    e.FromName,
                    e.ToName,
                    e.FromSpeciesId,
                    e.ToSpeciesId,
                }
            ),
            MoveLearned e => ("MoveLearned", new { e.CreatureName, e.MoveName }),
            MoveReplacementRequired e => (
                "MoveReplacementRequired",
                new
                {
                    e.CreatureName,
                    e.NewMoveName,
                    e.CurrentMoves,
                }
            ),
            MoveForgotten e => ("MoveForgotten", new { e.CreatureName, e.MoveName }),
            MoveLearnDeclined e => ("MoveLearnDeclined", new { e.CreatureName, e.MoveName }),
            _ => ("Unknown", new { }),
        };

    // Projects one reward-choice option to the wire: a discriminated "kind" plus the fields the modal card
    // needs. An item carries its id/name/rarity (rarity colours the card); a gold bag carries its amount. Kept
    // flat (not a nested union) so the TypeScript client reads one shape and branches on Kind.
    private static object ProjectRewardOption(RewardOption option) =>
        option switch
        {
            ItemRewardOption i => new
            {
                Kind = "item",
                i.ItemId,
                ItemName = (string?)i.ItemName,
                Rarity = (string?)i.Rarity.ToString(),
                Gold = 0,
            },
            GoldRewardOption g => new
            {
                Kind = "gold",
                ItemId = 0,
                ItemName = (string?)null,
                Rarity = (string?)null,
                g.Gold,
            },
            _ => new
            {
                Kind = "unknown",
                ItemId = 0,
                ItemName = (string?)null,
                Rarity = (string?)null,
                Gold = 0,
            },
        };

    // Projects one shop stock item to the wire shape the client's shop modal reads (id + name + price + rarity
    // colour). Mirrors ProjectRewardOption's field-level projection — the recurring web event field-projection
    // gap (a new ShopOfferItem field is invisible on the wire until it's added here).
    private static object ProjectShopItem(ShopOfferItem item) =>
        new
        {
            item.ItemId,
            ItemName = (string?)item.ItemName,
            item.Price,
            Rarity = item.Rarity.ToString(),
        };
}
