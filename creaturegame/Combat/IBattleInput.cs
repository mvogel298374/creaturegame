using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>What a side chose to do this turn: use a move (FIGHT) or use a bag item (ITEM).</summary>
public abstract record TurnChoice;

/// <summary>FIGHT: the selected move for this turn.</summary>
public sealed record MoveTurnChoice(PokemonAttack Move) : TurnChoice;

/// <summary>ITEM: use a bag item on the user's creature. <paramref name="TargetMoveSlot"/> is the move
/// slot (0–3) a single-move PP restore targets; null for everything else.</summary>
public sealed record ItemTurnChoice(Item Item, int? TargetMoveSlot = null) : TurnChoice;

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
    /// The player's whole-turn choice: FIGHT (a move) or ITEM (a bag item). Only consulted for a side that
    /// can actually open the menu — <see cref="Battle"/> still resolves lock-in/Struggle first, and only the
    /// player side has a bag. The default delegates to <see cref="ChooseMoveAsync"/> (FIGHT only), so AI /
    /// automated inputs never need to know about items; only the interactive <c>SignalRInput</c> overrides
    /// this to offer the bag.
    /// </summary>
    async Task<TurnChoice> ChooseTurnActionAsync(TurnContext context) =>
        new MoveTurnChoice(await ChooseMoveAsync(context));

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
