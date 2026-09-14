using creaturegame.Combat;
using creaturegame.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace creaturegame.Web.Battle;

public sealed class SignalRBattleEventEmitter(
    IHubContext<BattleHub, IBattleClient> hubContext,
    Func<string?> currentConnectionId
) : IBattleEventEmitter
{
    // The "state-establishing" events, cached as they pass through — everything a client that lost all
    // in-memory state (a full SPA remount, not just a transport drop) needs to know what's on screen right now.
    // Each otherwise fires exactly once, to whichever connection was current at the time, so a reconnecting
    // client would otherwise be stuck with no way to reach its current phase — see ReplayLastKnownState /
    // GameSessionManager.ReEstablishClient / ARCHITECTURE.md §2.7. Cached HERE rather than reconstructed by the
    // session layer because this is the one place each is already fully assembled (e.g. TurnStarted's live
    // STAB/effectiveness/CanSwitch) — reconstructing an equivalent elsewhere would risk drifting from the
    // engine's real logic. `volatile`: Emit runs on the run's background task thread while ReplayLastKnownState
    // runs on the SignalR hub thread (a reconnect can land concurrently with a live Emit).
    //
    // Three different lifetimes:
    //  - Run-scoped (RegionMapRevealed): set at most once per run, never cleared.
    //  - Biome-scoped (BiomeEntered, BiomeNodePlanRevealed): replaced by the next biome's; NOT cleared by
    //    BattleEnded/RunEnded — the current biome persists across and after the battles fought in it.
    //  - Battle-scoped (BattleStarted, TurnStarted): cleared on BattleEnded/RunEnded — see that case below.
    private volatile RegionMapRevealed? _lastRegionMap;
    private volatile BiomeEntered? _lastBiomeEntered;
    private volatile BiomeNodePlanRevealed? _lastBiomeNodePlan;
    private volatile BattleStarted? _lastBattleStarted;
    private volatile TurnStarted? _lastTurnStarted;

    public void Emit(BattleEvent evt)
    {
        switch (evt)
        {
            case RegionMapRevealed map:
                _lastRegionMap = map;
                break;
            case BiomeEntered entered:
                _lastBiomeEntered = entered;
                _lastBiomeNodePlan = null; // the new biome's node plan hasn't rolled yet
                break;
            case BiomeNodePlanRevealed plan:
                _lastBiomeNodePlan = plan;
                break;
            case BattleStarted started:
                _lastBattleStarted = started;
                _lastTurnStarted = null; // the new battle hasn't reached its first turn yet
                break;
            case TurnStarted turn:
                _lastTurnStarted = turn;
                break;
            case BattleEnded or RunEnded:
                // No battle is live between encounters (a route/shop/reward/recovery/etc. prompt is a separate,
                // not-yet-replayed event category — ARCHITECTURE.md §2.7's noted follow-up gap). Replaying a
                // just-finished battle's stale state into one of those would be actively wrong, not just
                // incomplete, so only the battle-scoped half of the cache clears — the run/biome-scoped half
                // stays valid (it's still the actual current map/biome).
                _lastBattleStarted = null;
                _lastTurnStarted = null;
                break;
        }

        Send(evt);
    }

    // The actual per-connection dispatch — resolved per-call so events follow a reconnect. Dropping while
    // disconnected is safe by design, not a bug — see ARCHITECTURE.md §2.7. Split out from Emit so
    // ReplayLastKnownState can dispatch a cached event directly without re-entering Emit's cache-update switch
    // above — which would otherwise, e.g., see the replayed BattleStarted and wipe the very TurnStarted the
    // same replay is about to send next.
    private void Send(BattleEvent evt)
    {
        var connectionId = currentConnectionId();
        if (string.IsNullOrEmpty(connectionId))
            return;

        var (type, payload) = MapEvent(evt);
        _ = hubContext.Clients.Client(connectionId).OnBattleEvent(type, payload);
    }

    /// <summary>Re-sends every cached state-establishing event, in the order it would naturally occur, to
    /// whatever connection is current right now — each a no-op while its slice of the cache is empty (e.g. the
    /// battle-scoped pair between encounters, or the whole cache in the legacy endless chain, which never emits
    /// the biome/map events at all). See the cache fields' own doc for the lifetimes and what this deliberately
    /// doesn't yet cover.</summary>
    public void ReplayLastKnownState()
    {
        if (_lastRegionMap is { } map)
            Send(map);
        if (_lastBiomeEntered is { } entered)
            Send(entered);
        if (_lastBiomeNodePlan is { } plan)
            Send(plan);
        if (_lastBattleStarted is { } started)
            Send(started);
        if (_lastTurnStarted is { } turn)
            Send(turn);
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
                        m.Power,
                    }),
                    e.CanSwitch,
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
            BiomeNodePlanRevealed e => ("BiomeNodePlanRevealed", new { e.NodeKinds }),
            RegionMapRevealed e => (
                "RegionMapRevealed",
                new
                {
                    e.Width,
                    e.Height,
                    Biomes = e.Biomes.Select(b => new
                    {
                        b.Id,
                        b.Name,
                        Types = b.Types.Select(t => t.ToString()),
                        b.Neighbours,
                        b.X,
                        b.Y,
                    }),
                    Routes = e.Routes.Select(r => new
                    {
                        r.FromBiomeId,
                        r.ToBiomeId,
                        Cells = r.Cells.Select(c => new { c.X, c.Y }),
                    }),
                }
            ),
            RunPresentationRevealed e => (
                "RunPresentationRevealed",
                new { e.Generation, e.TypeRoster }
            ),
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
            AcquisitionOffered e => (
                "AcquisitionOffered",
                new
                {
                    e.Source,
                    e.SpeciesId,
                    e.Name,
                    e.Level,
                    Types = e.Types.Select(t => t.ToString()),
                    e.MaxHp,
                    e.PartyFull,
                    Party = e.Party.Select(ProjectPartyMember),
                }
            ),
            CreatureAcquired e => (
                "CreatureAcquired",
                new
                {
                    e.Name,
                    e.SpeciesId,
                    e.Replaced,
                    e.ReplacedName,
                }
            ),
            AcquisitionDeclined e => ("AcquisitionDeclined", new { e.Name }),
            PartyUpdated e => (
                "PartyUpdated",
                new { Members = e.Members.Select(ProjectPartyMember) }
            ),
            LeadChoiceOffered e => (
                "LeadChoiceOffered",
                new { Party = e.Party.Select(ProjectPartyMember) }
            ),
            LeadChanged e => ("LeadChanged", new { e.Name, e.SpeciesId }),
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
            Revived e => (
                "Revived",
                new
                {
                    e.CreatureName,
                    e.HpRestored,
                    e.HpAfter,
                }
            ),
            MoveUsed e => ("MoveUsed", new { e.AttackerName, e.MoveName }),
            MoveMissed e => ("MoveMissed", new { e.AttackerName, e.MoveName }),
            MoveHadNoEffect e => ("MoveHadNoEffect", new { e.TargetName, e.MoveName }),
            AlreadyAsleep e => ("AlreadyAsleep", new { e.TargetName }),
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
            ConfusionAlready e => ("ConfusionAlready", new { e.TargetName }),
            MoveFailed => ("MoveFailed", new { }),
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
            SwitchInOffered e => (
                "SwitchInOffered",
                new { Party = e.Party.Select(ProjectPartyMember), e.FaintedName }
            ),
            CreatureSwitchedIn e => (
                "CreatureSwitchedIn",
                new
                {
                    e.Name,
                    e.SpeciesId,
                    e.Level,
                    e.Hp,
                    e.MaxHp,
                    Status = e.Status.ToString(),
                }
            ),
            ExperienceGained e => (
                "ExperienceGained",
                new
                {
                    e.CreatureName,
                    e.Amount,
                    e.OnBench,
                }
            ),
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
                    e.OnBench,
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
                    e.ToSpeciesName,
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

    // Projects one reward-choice option to the wire: a discriminated "kind" plus every arm's fields flattened
    // into the *same* shape (one anonymous type), so the TypeScript client reads one shape and branches on Kind.
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
                HpRestore = 0,
                CureStatus = false,
                RestoreLowPp = false,
                Label = (string?)null,
            },
            GoldRewardOption g => new
            {
                Kind = "gold",
                ItemId = 0,
                ItemName = (string?)null,
                Rarity = (string?)null,
                g.Gold,
                HpRestore = 0,
                CureStatus = false,
                RestoreLowPp = false,
                Label = (string?)null,
            },
            HealRewardOption h => new
            {
                Kind = "heal",
                ItemId = 0,
                ItemName = (string?)null,
                Rarity = (string?)null,
                Gold = 0,
                h.HpRestore,
                h.CureStatus,
                h.RestoreLowPp,
                Label = (string?)h.Label,
            },
            _ => new
            {
                Kind = "unknown",
                ItemId = 0,
                ItemName = (string?)null,
                Rarity = (string?)null,
                Gold = 0,
                HpRestore = 0,
                CureStatus = false,
                RestoreLowPp = false,
                Label = (string?)null,
            },
        };

    // Field-level projection gap: a new ShopOfferItem field is invisible on the wire until it's added here
    // (TODO.md — the recurring web-event field-projection gap).
    private static object ProjectShopItem(ShopOfferItem item) =>
        new
        {
            item.ItemId,
            ItemName = (string?)item.ItemName,
            item.Price,
            Rarity = item.Rarity.ToString(),
        };

    // Shared by AcquisitionOffered/PartyUpdated (same field-projection-gap concern as ProjectShopItem).
    // internal (not private) so GameSessionManager.GetParty's pulled snapshot matches the pushed events exactly.
    internal static object ProjectPartyMember(PartyMemberInfo m) =>
        new
        {
            m.SpeciesId,
            m.Name,
            m.Level,
            m.Hp,
            m.MaxHp,
            Status = m.Status.ToString(),
            m.IsLead,
        };
}
