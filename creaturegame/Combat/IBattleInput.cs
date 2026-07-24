using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>What a side chose to do this turn: use a move (FIGHT), use a bag item (ITEM), or swap the active
/// creature for a benched one (SWITCH). Struggle is the system fallback when FIGHT is chosen with nothing
/// selectable.</summary>
public abstract record TurnChoice;

/// <summary>FIGHT: the selected move for this turn.</summary>
public sealed record MoveTurnChoice(PokemonAttack Move) : TurnChoice;

/// <summary>ITEM: use a bag item. <paramref name="TargetMoveSlot"/> is the move slot (0–3) a single-move PP
/// restore targets; <paramref name="TargetPartySlot"/> is the party-member index a Revive targets (a fainted
/// benched member). Both null for the ordinary self-targeting items (they act on the active creature).</summary>
public sealed record ItemTurnChoice(
    Item Item,
    int? TargetMoveSlot = null,
    int? TargetPartySlot = null
) : TurnChoice;

/// <summary>SWITCH: swap the active creature out for the benched party member at <paramref name="PartyIndex"/>
/// (the voluntary in-battle switch — In-Combat Switching Stage A). <see cref="Battle"/> validates the pick
/// (in range, alive, not the already-active member, and the active creature isn't trapped by a partial-trap
/// bind); an illegal pick falls back to FIGHT rather than stranding the turn.</summary>
public sealed record SwitchTurnChoice(int PartyIndex) : TurnChoice;

/// <summary>FIGHT chosen with no selectable move — resolves to Struggle. Gen 1 shows the full menu even out of
/// PP (BAG/SWITCH stay reachable); only <em>choosing FIGHT</em> with nothing usable Struggles. This is the
/// whole-turn choice a non-interactive input returns when it is out of PP (it has no BAG/SWITCH to offer), so
/// the default <see cref="IBattleInput.ChooseTurnActionAsync"/> never has to call <see cref="IBattleInput.ChooseMoveAsync"/>
/// on a moveless creature.</summary>
public sealed record StruggleTurnChoice : TurnChoice
{
    public static readonly StruggleTurnChoice Instance = new();
}

/// <summary>
/// Abstracts move selection for one side of a battle.
/// Assign one implementation per side when constructing <see cref="Battle"/>.
///
/// Implementations: AutoSelectInput (current default), ConsoleInput (Priority 7),
/// RandomMoveInput / GreedyAIInput / WeightedAIInput (Priority 10).
///
/// This interface is only called when a real choice exists. Struggle is a
/// system-enforced fallback — when the creature has no selectable move
/// (<see cref="Creatures.Creature.CanSelectAnyMove"/> is false: out of PP, or its only
/// move is Disabled), Battle bypasses IBattleInput and passes null directly to AttackAction.
/// </summary>
public interface IBattleInput
{
    Task<PokemonAttack> ChooseMoveAsync(TurnContext context);

    /// <summary>
    /// The player's whole-turn choice: FIGHT (a move), ITEM (a bag item), or SWITCH (swap the active creature).
    /// Only consulted for a side that can actually open the menu — <see cref="Battle"/> still resolves true
    /// lock-in first, and only the player side has a bag / party. The default delegates to
    /// <see cref="ChooseMoveAsync"/> (FIGHT only), so AI / automated inputs never need to know about items or
    /// switching; only the interactive <c>SignalRInput</c> overrides this to offer the bag / SWITCH.
    /// <para>Unlike <see cref="ChooseMoveAsync"/> this is now consulted even when the creature is out of PP (Gen 1
    /// keeps the menu open so BAG/SWITCH stay reachable), so the default must NOT call <see cref="ChooseMoveAsync"/>
    /// on a moveless creature (it would throw); it returns <see cref="StruggleTurnChoice"/> instead, which
    /// <see cref="Battle"/> resolves to Struggle.</para>
    /// </summary>
    async Task<TurnChoice> ChooseTurnActionAsync(TurnContext context) =>
        context.Attacker.CanSelectAnyMove
            ? new MoveTurnChoice(await ChooseMoveAsync(context))
            : StruggleTurnChoice.Instance;

    /// <summary>
    /// Asked when the creature levels into a new move but its four slots are full: return the slot index
    /// (0–3) to forget and replace, or <c>null</c> to decline (keep the current moveset). Only the player
    /// input is ever consulted. The default is to decline, so AI / automated inputs never block on a
    /// level-up prompt — only an interactive input (the web <c>SignalRInput</c>) overrides it.
    /// </summary>
    Task<int?> ChooseMoveToForgetAsync(MoveReplacementContext context) =>
        Task.FromResult<int?>(null);

    /// <summary>
    /// Asked between encounters when a roguelite "Poké Center" recovery is offered: return <c>true</c> to fully
    /// restore the creature (HP/PP/status), <c>false</c> to skip it. Only the interactive player input is ever
    /// consulted; the default accepts, so automated / AI inputs never block on the prompt and the chain still
    /// heals on schedule — only the web <see cref="SignalRInput"/> blocks awaiting a real choice.
    /// </summary>
    Task<bool> ConfirmRecoveryAsync(RecoveryContext context) => Task.FromResult(true);

    /// <summary>
    /// Asked between encounters when an evolution is offered (after a level-up crosses the threshold): return
    /// <c>true</c> to let it evolve, <c>false</c> to cancel (Gen 1 B-cancel — it re-offers at the next
    /// level-up). Only the interactive player input is ever consulted; the default allows it, so automated /
    /// AI inputs never block on the prompt and still evolve on schedule — only the web
    /// <see cref="SignalRInput"/> blocks awaiting a real choice.
    /// </summary>
    Task<bool> ConfirmEvolutionAsync(EvolutionPromptContext context) => Task.FromResult(true);

    /// <summary>
    /// Asked at the start of each biome (run start, and after each Poké Center) when the player charts the next
    /// leg of the route: return the <see cref="BiomeDefinition.Id"/> of the biome to enter from the offered
    /// <see cref="BiomeChoiceContext.Options"/>. Only the interactive player input is ever consulted; the default
    /// takes the first option, so automated / AI inputs never block on the map prompt and a headless run still
    /// auto-pilots the route — only the web <see cref="SignalRInput"/> blocks awaiting a real choice.
    /// </summary>
    Task<string> ChooseBiomeAsync(BiomeChoiceContext context) =>
        Task.FromResult(context.Options[0].Id);

    /// <summary>
    /// Asked whenever a rolled reward is offered as a pick-one-of-N choice (two rarity-rolled items or a larger
    /// gold bag — every reward-earning node now offers one): return the index of the chosen option. Blocks the
    /// run until the player picks. Only the interactive player input is ever consulted; the default takes option
    /// 0 (the first item), so automated / AI inputs never stall on the modal and a headless run still auto-picks
    /// — only the web <see cref="SignalRInput"/> blocks awaiting a real choice. An out-of-range index is clamped
    /// to the first option downstream, so a stale / malformed pick never strands the run.
    /// </summary>
    Task<int> ChooseRewardAsync(RewardChoiceContext context) => Task.FromResult(0);

    /// <summary>
    /// Asked whenever an acquisition is offered (the themed draft after a win; later the boss catch): return an
    /// <see cref="AcquisitionDecision"/> — decline, add to the party, or (when the party is full) add by
    /// replacing a chosen member. Blocks the run until the player answers. Only the interactive player input is
    /// ever consulted; the default <em>declines</em>, so automated / AI inputs never stall on the offer and a
    /// headless run simply never builds a party — only the web <see cref="SignalRInput"/> blocks awaiting a real
    /// choice. A decline (or an accept the roster can't honour) is a pure sequencing no-op downstream.
    /// </summary>
    Task<AcquisitionDecision> ChooseAcquisitionAsync(AcquisitionContext context) =>
        Task.FromResult(AcquisitionDecision.Decline);

    /// <summary>
    /// Asked at a biome boundary (after the Poké Center, before the route choice) when the party holds more than
    /// one creature: return the <see cref="Creatures.Party.Members"/> index of the creature to lead into the next
    /// biome. Blocks the run until the player picks. Only the interactive player input is ever consulted; the
    /// default keeps the <em>current</em> lead (<see cref="AcquisitionContext"/>-style no-op), so automated / AI
    /// inputs never stall on the prompt and a headless run keeps its lead. An out-of-range / unchanged pick is a
    /// no-op downstream. This is a between-biome choice only — not in-battle switching.
    /// </summary>
    Task<int> ChooseLeadAsync(LeadChoiceContext context) =>
        Task.FromResult(context.Party.LeadIndex);

    /// <summary>
    /// Asked repeatedly while the player is in a Shop node: return a <see cref="BuyShopItem"/> to purchase a
    /// stock item, or <see cref="LeaveShop"/> to end the visit — the shop event loops on this until the player
    /// leaves. Only the interactive player input is ever consulted; the default leaves immediately, so AI /
    /// automated inputs never buy and a headless run walks straight past the shop — only the web
    /// <see cref="SignalRInput"/> blocks awaiting real buy/leave choices. An out-of-range / unaffordable buy is
    /// a no-op downstream, so a stale pick never strands the run.
    /// </summary>
    Task<ShopAction> ChooseShopActionAsync(ShopContext context) =>
        Task.FromResult<ShopAction>(LeaveShop.Instance);

    /// <summary>
    /// Asked mid-battle when the active creature faints but a bench member is still alive (the forced
    /// faint-switch, Phase 4 Stage 3): return the <see cref="Creatures.Party.Members"/> index of the creature to
    /// send in against the same enemy. A <em>forced</em> choice — unlike the between-biome lead pick it can't be
    /// declined (the alternative is losing the run). Only the interactive player input is ever consulted; the
    /// default sends in the first <em>alive</em> member, so automated / AI inputs never stall on the prompt and
    /// never send in a fainted creature — only the web <see cref="SignalRInput"/> blocks awaiting a real choice.
    /// A stale / out-of-range / <em>fainted</em> pick is corrected to the first live member downstream
    /// (<see cref="Battle"/>), so a malformed client can never send in a downed creature.
    /// </summary>
    Task<int> ChooseSwitchInAsync(SwitchInContext context)
    {
        for (int i = 0; i < context.Party.Count; i++)
            if (context.Party.Members[i].IsAlive())
                return Task.FromResult(i);
        return Task.FromResult(context.Party.LeadIndex);
    }
}

/// <summary>Context for a level-up move-replacement decision: who is learning, and the move on offer.</summary>
public sealed record MoveReplacementContext(Creature Learner, Attack NewMove);

/// <summary>Context for a between-biome route choice: the biomes on offer (the current biome's playable
/// neighbours, or the run's opening set). An input picks one by <see cref="BiomeDefinition.Id"/>.</summary>
public sealed record BiomeChoiceContext(IReadOnlyList<BiomeDefinition> Options);

/// <summary>Context for a between-encounter Poké Center recovery offer: the player creature and the running
/// win count (so an input could vary its answer by depth — the default just accepts).</summary>
public sealed record RecoveryContext(Creature Player, int BattlesWon);

/// <summary>Context for a between-encounter evolution offer: the player creature and the form it would become
/// (id + display name), so an input could decide based on it — the default just allows.</summary>
public sealed record EvolutionPromptContext(Creature Player, int ToSpeciesId, string ToName);

/// <summary>Context for a reward-choice offer: the earning source label (<c>"Battle"</c> / the
/// <c>RunNodeKind</c> name) and the options on offer (two rarity-rolled items and a gold bag by default). An
/// input picks one by index.</summary>
public sealed record RewardChoiceContext(string Source, IReadOnlyList<RewardOption> Options);

/// <summary>Context for a shop buy/leave decision: the stock on offer this visit and the player's current
/// <see cref="Balance"/> in ₽ (so an input can gate its choice on affordability — the default just leaves).</summary>
public sealed record ShopContext(IReadOnlyList<ShopOfferItem> Items, int Balance);

/// <summary>Context for an acquisition offer: the <see cref="Offered"/> creature and the current
/// <see cref="Party"/> (so an interactive input can present the swap-out choice when the roster is full). The
/// <see cref="Source"/> is the channel label (<c>"ThemedDraft"</c> / <c>"BossCatch"</c>).</summary>
public sealed record AcquisitionContext(Creature Offered, Party Party, string Source);

/// <summary>Context for a between-biome lead choice: the current <see cref="Party"/> (the members to pick from,
/// the active one at <see cref="Creatures.Party.LeadIndex"/>). An input returns the chosen member index; the
/// default keeps the current lead.</summary>
public sealed record LeadChoiceContext(Party Party);

/// <summary>Context for a forced faint-switch (Phase 4 Stage 3): the current <see cref="Party"/> — the active
/// creature at <see cref="Creatures.Party.LeadIndex"/> has just fainted, so an input returns the index of a live
/// member to send in against the same enemy (the default picks the first alive one). Carries the live
/// <see cref="Creatures.Party"/> so an input can inspect each member's <see cref="Creature.IsAlive"/> state.</summary>
public sealed record SwitchInContext(Party Party);

/// <summary>The player's answer to an acquisition offer: <see cref="Accept"/> false = decline (roster
/// unchanged); accept with a null <see cref="ReplaceSlot"/> = add to a party with room; accept with a
/// <see cref="ReplaceSlot"/> index = add by swapping out that member (the full-party path). An accept the roster
/// can't honour (full party with no valid slot) is treated as a decline downstream, so a stale pick never
/// strands the run.</summary>
public sealed record AcquisitionDecision(bool Accept, int? ReplaceSlot)
{
    /// <summary>Decline the offer — leave the roster as-is (the default for automated / AI inputs).</summary>
    public static readonly AcquisitionDecision Decline = new(false, null);

    /// <summary>Accept into an open party slot.</summary>
    public static AcquisitionDecision Add() => new(true, null);

    /// <summary>Accept by replacing the member at <paramref name="slot"/> (the full-party swap path).</summary>
    public static AcquisitionDecision Replace(int slot) => new(true, slot);
}
