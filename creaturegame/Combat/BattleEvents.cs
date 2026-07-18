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

/// <summary>A between-biome route choice is offered: the candidate biomes (the current biome's playable
/// neighbours, or the run's opening set) the player picks the next leg from. A blocking event — the run loop
/// awaits the player's pick via <see cref="IBattleInput.ChooseBiomeAsync"/> before continuing, so the client
/// raises the map screen here. Each option carries id + display name + type theme for the biome card.</summary>
public record BiomeChoiceOffered(IReadOnlyList<BiomeOption> Options) : BattleEvent;

/// <summary>One biome on offer in a <see cref="BiomeChoiceOffered"/>: stable id, display name, and the type
/// theme (1–3 types) for the card's badges.</summary>
public record BiomeOption(string Id, string Name, IReadOnlyList<DamageType> Types);

/// <summary>The player entered a biome (after choosing it / at run start). Carries the biome's id, name and
/// type theme so the client can title and theme the next leg of the run. Followed by that biome's encounters.</summary>
public record BiomeEntered(string BiomeId, string BiomeName, IReadOnlyList<DamageType> Types)
    : BattleEvent;

/// <summary>The run reached a route node. <see cref="Kind"/> is the <c>RunNodeKind</c> name. In biome mode this
/// fires once per node <em>in plan order</em> — including <c>WildBattle</c> — so the encounter-map overlay can
/// advance its position pin uniformly; the client titles a text banner only for the standout kinds
/// (Elite/Boss/Shop/Treasure/Mystery) and filters <c>WildBattle</c> out of the log (a plain wild encounter has
/// no banner, as before). The legacy endless chain (no biome map) never emits it for a wild node.</summary>
public record RunNodeEntered(string Kind) : BattleEvent;

/// <summary>Emitted once when a biome is entered and its seeded node route is rolled — the ordered
/// <c>RunNodeKind</c> names the encounter-map overlay draws as the biome's ladder ahead of time (wild / elite /
/// shop / treasure / mystery … capped by the <c>BossBattle</c> apex). Revealing the plan is a pure presentation
/// signal: it does <em>not</em> change sequencing (the plan is already deterministic once the biome is entered —
/// <c>GAME_LOOP.md §4</c>). Follows the <see cref="BiomeEntered"/> for that biome.</summary>
public record BiomeNodePlanRevealed(IReadOnlyList<string> NodeKinds) : BattleEvent;

/// <summary>Emitted once at the start of a biome-mode run: the whole playable biome subset (the seeded 10-of-18)
/// so the client can draw the region as a node-link map. Each <see cref="RegionMapBiome"/> carries its id, name,
/// type theme, and the ids of its playable neighbours (the graph edges) — the client wires the overworld map and
/// traces the route through it as <see cref="BiomeEntered"/> events arrive. Static for the run (same seed ⇒ same
/// map); the legacy endless chain never emits it.</summary>
public record RegionMapRevealed(IReadOnlyList<RegionMapBiome> Biomes) : BattleEvent;

/// <summary>One biome node on the region map: stable id, display name, type theme (for the waypoint colour), the
/// ids of its neighbours <em>within the playable subset</em> (edges to draw), and its authored 2-D map position
/// (<see cref="MapX"/> / <see cref="MapY"/>, 0–100 each, y down). Neighbours outside the subset are filtered out
/// server-side so the client never references a biome it wasn't sent.</summary>
public record RegionMapBiome(
    string Id,
    string Name,
    IReadOnlyList<DamageType> Types,
    IReadOnlyList<string> Neighbours,
    int MapX,
    int MapY
);

/// <summary>A reward roll paid out gold and/or items — a battle win (inline, <paramref name="Source"/> =
/// <c>"Battle"</c>) or a Treasure/Mystery node (<paramref name="Source"/> = the <c>RunNodeKind</c> name, which
/// blocks on an ack so the client can raise a modal instead of an inline log line). <paramref name="GoldTotal"/>
/// is the wallet balance after crediting, so the HUD can set rather than accumulate.</summary>
public record RewardGranted(string Source, int Gold, int GoldTotal, IReadOnlyList<string> ItemNames)
    : BattleEvent;

/// <summary>A rolled reward is offered to the player as a pick-one-of-N choice (two rarity-rolled items or a
/// larger gold bag). A blocking event — the run loop awaits the player's pick via
/// <see cref="IBattleInput.ChooseRewardAsync"/> before applying the chosen option, so the client raises the
/// reward-choice modal here. <paramref name="Source"/> is the earning node (<c>"Battle"</c> / the
/// <c>RunNodeKind</c> name). The chosen option is then applied and announced by a following
/// <see cref="RewardGranted"/>. Each <see cref="RewardOption"/> is an item (id + name + rarity) or a gold bag.</summary>
public record RewardChoiceOffered(string Source, IReadOnlyList<RewardOption> Options) : BattleEvent;

/// <summary>A Shop node opened with this stock (<paramref name="Items"/>) and the player's current
/// <paramref name="Balance"/> in ₽. A blocking event — the run loop awaits the player's buy/leave choices via
/// <see cref="IBattleInput.ChooseShopActionAsync"/>, so the client raises the shop modal here. Each purchase is
/// announced by a following <see cref="ShopItemPurchased"/>; the visit ends when the player leaves.</summary>
public record ShopOffered(IReadOnlyList<ShopOfferItem> Items, int Balance) : BattleEvent;

/// <summary>The player bought <paramref name="ItemName"/> for <paramref name="Price"/>₽; <paramref name="Balance"/>
/// is the wallet balance after the spend (so the HUD/modal can set rather than subtract). The item was added to
/// the bag.</summary>
public record ShopItemPurchased(string ItemName, int Price, int Balance) : BattleEvent;

// --- Acquisition (party roster — themed draft / boss catch, ENCOUNTER_DESIGN.md §4) ---
/// <summary>A creature is offered to add to the party — the reusable acquisition offer both channels raise (the
/// themed draft after a win, and later the boss catch). A blocking event: the run loop awaits the player's
/// accept/decline (and, when the party is full, which member to swap out) via
/// <see cref="IBattleInput.ChooseAcquisitionAsync"/> before continuing, so the client raises the offer modal here.
/// The offered creature's display fields ride flat (<paramref name="SpeciesId"/> for the sprite,
/// <paramref name="Name"/>/<paramref name="Level"/>/<paramref name="Types"/>/<paramref name="MaxHp"/> for the
/// card); <paramref name="PartyFull"/> + the current <paramref name="Party"/> snapshot let the modal show the
/// swap-out choice when the roster is at its cap. <paramref name="Source"/> is the channel
/// (<c>"ThemedDraft"</c> / <c>"BossCatch"</c>).</summary>
public record AcquisitionOffered(
    string Source,
    int SpeciesId,
    string Name,
    int Level,
    IReadOnlyList<DamageType> Types,
    int MaxHp,
    bool PartyFull,
    IReadOnlyList<PartyMemberInfo> Party
) : BattleEvent;

/// <summary>A snapshot of one party member for the client's roster panel + acquisition swap picker: species id
/// (sprite), name, level, current/max HP, major status, and whether it is the active <see cref="IsLead"/>. The
/// lead is excluded as a swap target client-side (a mid-biome lead change is Stage 1d, not this offer).</summary>
public record PartyMemberInfo(
    int SpeciesId,
    string Name,
    int Level,
    int Hp,
    int MaxHp,
    StatusCondition Status,
    bool IsLead
);

/// <summary>Projects a <see cref="Party"/> into the <see cref="PartyMemberInfo"/> snapshot the client's roster
/// panel + acquisition swap picker read. The single source of truth for that shape — used by the run loop when
/// it emits <see cref="PartyUpdated"/> / <see cref="AcquisitionOffered"/> and by the web layer's on-demand party
/// hydrate endpoint, so the pushed event and the pulled snapshot can never drift apart. The member at the party's
/// lead index is flagged <see cref="PartyMemberInfo.IsLead"/> (the client excludes it as a swap target).</summary>
public static class PartyProjection
{
    public static IReadOnlyList<PartyMemberInfo> Snapshot(Party party)
    {
        var members = new List<PartyMemberInfo>(party.Count);
        for (int i = 0; i < party.Count; i++)
        {
            var c = party.Members[i];
            members.Add(
                new PartyMemberInfo(
                    c.SpeciesId,
                    c.Name,
                    c.Level,
                    c.Attributes.HP,
                    c.Attributes.MaxHP,
                    c.Battle.Status,
                    IsLead: i == party.LeadIndex
                )
            );
        }
        return members;
    }
}

/// <summary>The offered creature was added to the party (accepted). <paramref name="Replaced"/> is true when it
/// took a full party's slot (<paramref name="ReplacedName"/> = the released member); false when it filled an
/// empty slot. Followed by a <see cref="PartyUpdated"/> snapshot.</summary>
public record CreatureAcquired(string Name, int SpeciesId, bool Replaced, string? ReplacedName)
    : BattleEvent;

/// <summary>The player declined an acquisition offer — the roster is unchanged and the run advances (a decline
/// is a pure sequencing no-op). Drives the "left it in the wild" line.</summary>
public record AcquisitionDeclined(string Name) : BattleEvent;

/// <summary>The party roster changed — the full member snapshot the client's party panel re-renders from.
/// Emitted after an acquisition deposit; also the vehicle for surfacing a whole-party Poké Center heal (benched
/// members' restored HP) that the lead-only <see cref="PlayerRecovered"/> doesn't carry.</summary>
public record PartyUpdated(IReadOnlyList<PartyMemberInfo> Members) : BattleEvent;

/// <summary>A between-biome lead choice is offered (Phase 4 Stage 1d): pick which party member leads into the
/// next biome. Fires at the biome boundary — after the Poké Center, before the route choice — only when the
/// party holds more than one creature. A blocking event: the run loop awaits the player's pick via
/// <see cref="IBattleInput.ChooseLeadAsync"/> before continuing, so the client raises the lead-select modal here.
/// Carries the current roster snapshot (the picked-from members, the active one flagged
/// <see cref="PartyMemberInfo.IsLead"/>). Purely a between-biome choice — this is <em>not</em> in-battle
/// switching (that stays a separate, later feature; the battle engine is untouched).</summary>
public record LeadChoiceOffered(IReadOnlyList<PartyMemberInfo> Party) : BattleEvent;

/// <summary>The party's lead was reassigned (the between-biome lead choice picked a different member). The named
/// creature is now the active <c>RunState.Player</c> for the next biome. Followed by a <see cref="PartyUpdated"/>
/// snapshot (with the new lead flagged). Not emitted when the player keeps the current lead (a no-op).</summary>
public record LeadChanged(string Name, int SpeciesId) : BattleEvent;

// --- Move actions ---
public record MoveUsed(string AttackerName, string MoveName) : BattleEvent;

public record MoveMissed(string AttackerName, string MoveName) : BattleEvent;

/// <summary>The move hit but the target is immune (type-based) so nothing happened — "It doesn't affect …".</summary>
public record MoveHadNoEffect(string TargetName, string MoveName) : BattleEvent;

/// <summary>A sleep move hit a target that is already asleep — Gen 1's distinct "… is already asleep!"
/// (AlreadyAsleepText), as opposed to the generic "It doesn't affect …" every other already-statused case uses.</summary>
public record AlreadyAsleep(string TargetName) : BattleEvent;

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

// --- Items (using a bag item in battle) ---
/// <summary>A bag item was used on a creature this turn (e.g. "Used POTION on PIKACHU"). Emitted before
/// the item's effect events (Healed / StatusCleared / StatStageChanged / PpRestored).</summary>
public record ItemUsed(string ItemName, string TargetName) : BattleEvent;

/// <summary>A PP-restore item refilled a move's PP. One per move restored (Elixir restores all four).</summary>
public record PpRestored(string CreatureName, string MoveName, int PpAfter) : BattleEvent;

/// <summary>An item selection had no effect (e.g. a Potion at full HP, an Antidote with no poison, a ball
/// or revive in single-creature scope). Nothing is consumed. The Gen 1 "It won't have any effect!" line.</summary>
public record ItemUseFailed(string ItemName) : BattleEvent;

// --- Status conditions ---
public record StatusApplied(string TargetName, StatusCondition Status) : BattleEvent;

public record StatusDamage(string TargetName, int Damage, StatusCondition Source, int HpAfter)
    : BattleEvent;

public record StatusCleared(string CreatureName, StatusCondition WasStatus) : BattleEvent;

public record ActionBlocked(string CreatureName, StatusCondition Reason) : BattleEvent;

// --- Confusion (pseudo-status — separate from StatusCondition enum) ---
public record ConfusionStarted(string TargetName) : BattleEvent;

// A dedicated confusion move that hits an already-confused target, when the ruleset names the redundancy
// ("… is already confused!") — the Gen 3+ arm of IBattleRules.RedundantConfusionAnnouncement. Gen 1 instead
// emits the generic MoveFailed ("But it failed!"); neither ever re-rolls the confusion counter.
public record ConfusionAlready(string TargetName) : BattleEvent;

// The generic "But it failed!" line (pokered ConditionalPrintButItFailed) — Gen 1's message for a dedicated
// confusion move (Confuse Ray / Supersonic) that fails on an already-confused target.
public record MoveFailed : BattleEvent;

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

/// <summary>A creature was scared off by Roar/Whirlwind and fled — the wild battle ends with no faint
/// (Gen 1). <see cref="IsPlayer"/> distinguishes the player being blown away from the wild foe fleeing, so
/// the client can word it ("… was blown away!" vs "The wild … fled!"). Emitted instead of
/// <see cref="BattleEnded"/>; the run loop advances the encounter as neither a win nor a loss.</summary>
public record CreatureFled(string Name, bool IsPlayer) : BattleEvent;

// --- Forced switch on faint (Encounter Logic Phase 4 Stage 3) ---
/// <summary>The player's active creature fainted <em>but the party still has a live bench member</em>, so the run
/// does not end — the player must send in a replacement. A blocking event: <see cref="Battle"/> awaits the pick via
/// <see cref="IBattleInput.ChooseSwitchInAsync"/> before continuing (against the same enemy), so the client raises
/// the forced (non-dismissable) switch-in modal here. Carries the current roster snapshot (the members to pick
/// from — the fainted one reads HP 0 and is not a valid target) and the <paramref name="FaintedName"/> that just
/// dropped. This is a <em>forced</em> faint-switch; voluntary in-battle switching is a separate, later feature.</summary>
public record SwitchInOffered(IReadOnlyList<PartyMemberInfo> Party, string FaintedName)
    : BattleEvent;

/// <summary>A replacement creature was sent in after the active one fainted (the forced faint-switch). It battles
/// the same enemy from the next turn. Carries the incoming creature's <paramref name="SpeciesId"/> (the client
/// swaps the player sprite), <paramref name="Name"/> (the nameplate + the player/enemy side split for later
/// events), its <paramref name="Level"/> (the nameplate level — <see cref="TurnStarted"/> carries no level), and
/// its current HP / status (the nameplate bars). Followed by a <see cref="PartyUpdated"/> snapshot (with the new
/// active creature flagged and the fainted member at HP 0).</summary>
public record CreatureSwitchedIn(
    string Name,
    int SpeciesId,
    int Level,
    int Hp,
    int MaxHp,
    StatusCondition Status
) : BattleEvent;

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

/// <summary>An evolution is offered between encounters (after a level-up crosses the threshold). A blocking
/// event: the run loop awaits the player's allow/cancel decision via
/// <see cref="IBattleInput.ConfirmEvolutionAsync"/> before continuing, so the client raises the evolution
/// modal here. Carries both names + the from/to species ids so the modal can show the creature and what it
/// would become. On allow, <see cref="CreatureEvolved"/> follows; on cancel, <see cref="EvolutionCancelled"/>.</summary>
public record EvolutionOffered(string FromName, string ToName, int FromSpeciesId, int ToSpeciesId)
    : BattleEvent;

/// <summary>The player declined an offered evolution (Gen 1 B-cancel). The creature keeps its current form;
/// it will be offered again at the next level-up while still eligible. Drives the "stopped evolving" line.</summary>
public record EvolutionCancelled(string CreatureName) : BattleEvent;

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
