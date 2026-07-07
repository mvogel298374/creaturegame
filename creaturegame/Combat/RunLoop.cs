using creaturegame.Creatures;

namespace creaturegame.Combat;

/// <summary>
/// The kinds of node a biome's route can contain (<c>ENCOUNTER_DESIGN.md §5</c> / <c>GAME_LOOP.md §5</c>) — the
/// run-structure vocabulary the director's per-biome plan is written in. The three battle kinds map to an
/// <see cref="EncounterTier"/> for the enemy supplier; the three interaction kinds map to their own
/// <see cref="IRunEvent"/> bones. Generation-agnostic: a node kind is run structure, not a Gen 1 mechanic.
/// </summary>
public enum RunNodeKind
{
    WildBattle,
    EliteBattle,
    BossBattle,
    Shop,
    Treasure,
    Mystery,
}

/// <summary>
/// The difficulty <em>intent</em> a battle node carries to the enemy supplier — kept generation-agnostic in the
/// core (the same intent/mapping split as <c>DvQuality</c>): the web layer maps it to a concrete
/// <c>IEnemyArchetype</c> (Normal→Medium, Elite→Strong, Boss→Boss), which is web-layer roguelite tuning, not a
/// battle seam (<c>ENCOUNTER_DESIGN.md §3.1</c>). The core never names an archetype.
/// </summary>
public enum EncounterTier
{
    Normal,
    Elite,
    Boss,
}

/// <summary>
/// The run's logic state — the <em>only</em> thing <c>chooseNextEvent</c> reads to decide what happens next
/// (<c>GAME_LOOP.md §3/§4</c>). It is mutated by an event's <see cref="Outcome"/> (via the director's apply
/// step) and by the events themselves; never by player input directly — the player only changes an event's
/// outcome, which then feeds back here. Deterministic given <c>(state, rng)</c>.
/// </summary>
public sealed class RunState(Creature player)
{
    /// <summary>The one persistent creature the run is played with; its permanent half carries across events.</summary>
    public Creature Player { get; } = player;

    /// <summary>Encounters won so far. Drives the run summary and the legacy Poké Center milestone; in biome
    /// mode the foe-scaling depth axis is <see cref="RunDepth"/> instead (which also counts interaction nodes).</summary>
    public int BattlesWon { get; set; }

    /// <summary>
    /// The run's progression depth — <em>nodes traversed</em> (battle wins + interaction visits) — the axis the
    /// enemy supplier scales the next foe to (<c>ENCOUNTER_DESIGN.md §3.2</c>). In the legacy chain (battles
    /// only) it equals <see cref="BattlesWon"/>; in biome mode it also counts shop/treasure/mystery nodes, so a
    /// foe deeper in a biome — the Boss especially — scales harder than its win-count alone would imply.
    /// </summary>
    public int RunDepth { get; set; }

    /// <summary>
    /// Poké Center recoveries completed — one per heal milestone. <c>chooseNextEvent</c> compares this against
    /// <c>BattlesWon / healEveryN</c> so a recovery fires exactly once per milestone even though
    /// <see cref="BattlesWon"/> does not change while the recovery resolves.
    /// </summary>
    public int RecoveriesDone { get; set; }

    /// <summary>
    /// The player's major status to re-apply when the next encounter is built (Gen 1 keeps major status out of
    /// battle). Null means nothing carries. Captured after a win and cleared by a Poké Center heal.
    /// </summary>
    public CarriedStatus? CarriedStatus { get; set; }

    // --- Biome traversal (only meaningful when the director runs in biome mode; ignored by the legacy chain) ---

    /// <summary>
    /// The biome the run is currently in (the type theme its encounters draw from), or null before the first
    /// choice. Retained while the next route choice is offered so the biome's neighbours can be the options.
    /// </summary>
    public BiomeDefinition? CurrentBiome { get; set; }

    /// <summary>
    /// How many of the current biome's planned nodes have been resolved — the index into
    /// <see cref="BiomeNodePlan"/>. When it reaches the plan length the biome is cleared (Poké Center caps it,
    /// then the next route choice). Advances on every resolved node in the biome (battle won or interaction
    /// visited), not just battles.
    /// </summary>
    public int EventsInCurrentBiome { get; set; }

    /// <summary>
    /// The ordered node kinds of the current biome's route, generated (seeded) when the biome is entered. Empty
    /// before the first choice / in the legacy chain. <c>chooseNextEvent</c> dispatches
    /// <c>BiomeNodePlan[EventsInCurrentBiome]</c>; the final node is the biome's Boss
    /// (<c>ENCOUNTER_DESIGN.md §4</c>).
    /// </summary>
    public IReadOnlyList<RunNodeKind> BiomeNodePlan { get; set; } = [];

    /// <summary>
    /// True when the next event should be a route choice (run start, or after a biome's Poké Center). Set by the
    /// director in biome mode; the legacy chain never reads it.
    /// </summary>
    public bool NeedsBiomeChoice { get; set; } = true;
}

/// <summary>
/// The per-event context handed to every <see cref="IRunEvent"/> (<c>GAME_LOOP.md §3</c>): the run state plus
/// the shared output/input/RNG seams. Run-wide collaborators an event needs (type chart, move pool, enemy
/// supplier, …) are captured by the concrete events the <see cref="RunDirector"/> builds, not threaded here.
/// </summary>
public sealed record RunContext(
    RunState State,
    IBattleEventEmitter? Emitter,
    IBattleInput PlayerInput,
    IRandomSource? Rng
);

/// <summary>How valuable a rolled item reward is — Common is cheap/frequent, Epic is the premium tier
/// (<em>rarer = more expensive</em>; the web-layer <c>RewardCalculator</c> maps item cost → rarity, and skews
/// the roll upward with node tier + run depth). Run-economy vocabulary, generation-agnostic; carried on an
/// <see cref="ItemRewardOption"/> so the client can colour the choice card.</summary>
public enum RewardRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
}

/// <summary>One selectable option in a post-reward pick-one-of-N choice: either a specific item (with its
/// rolled rarity) or a gold bag. Ids/names/amounts are pre-resolved by the injected reward supplier (the
/// concrete web-layer <c>RewardCalculator</c>) so the core never touches the item catalog.</summary>
public abstract record RewardOption;

/// <summary>An item option: the resolved item id + display name and the rarity it rolled at.</summary>
public sealed record ItemRewardOption(int ItemId, string ItemName, RewardRarity Rarity)
    : RewardOption;

/// <summary>A gold-bag option — take this many ₽ instead of an item (the escape hatch when neither offered
/// item is useful).</summary>
public sealed record GoldRewardOption(int Gold) : RewardOption;

/// <summary>A rolled reward offered to the player as a pick-one-of-N choice (two rarity-rolled items and a
/// gold bag by default). <see cref="None"/> means nothing rolled (e.g. the ~15% no-drop on a wild win) — no
/// choice is offered. The player picks exactly one option, which is then applied and announced by a following
/// <see cref="RewardGranted"/>.</summary>
public sealed record RewardChoice(IReadOnlyList<RewardOption> Options)
{
    public static readonly RewardChoice None = new([]);

    public bool IsEmpty => Options.Count == 0;
}

/// <summary>What a reward roll needs to know about the moment it fires, handed to the injected reward supplier
/// (same pattern as the enemy supplier). <see cref="Source"/> is the node kind that earned the reward — battle
/// wins carry the beaten foe's <see cref="EnemyLevel"/> (0 for Treasure/Mystery, which have no foe) — letting
/// one supplier delegate dispatch to the right web-layer roll (battle vs Treasure vs Mystery) by node kind.</summary>
public sealed record RewardContext(RunNodeKind Source, int EnemyLevel, int Depth);

/// <summary>
/// The typed result of an event, which the <see cref="RunDirector"/> reads to advance the run
/// (<c>GAME_LOOP.md §3</c>). An event emits narration and may await input, then returns an <c>Outcome</c>; it
/// never decides what happens next. New event kinds add their own outcome record.
/// </summary>
public abstract record Outcome;

/// <summary>Outcome of a battle event: whether the player survived. A loss ends the run.</summary>
public sealed record BattleOutcome(bool Won) : Outcome;

/// <summary>
/// Outcome of a battle that ended because a side fled (Roar/Whirlwind in a wild encounter) — neither a win
/// nor a loss. The run continues (the player is alive), the node is consumed (advances the biome), and no XP
/// is awarded (nothing fainted). <see cref="PlayerFled"/> records who was scared off (the player blown away
/// vs the foe fleeing); both advance the run identically today.
/// </summary>
public sealed record FledOutcome(bool PlayerFled) : Outcome;

/// <summary>Outcome of a Poké Center recovery event: whether the player accepted the heal.</summary>
public sealed record RecoveryOutcome(bool Healed) : Outcome;

/// <summary>Outcome of a route-choice event: the biome the player elected to enter next.</summary>
public sealed record BiomeChoiceOutcome(BiomeDefinition Chosen) : Outcome;

/// <summary>
/// Outcome of an interaction-node bone (Shop / Treasure / Mystery): records which kind was visited so the
/// director advances the biome. These nodes have no behaviour yet (<c>ENCOUNTER_DESIGN.md §5</c> — bones now,
/// behaviour later); each graduates to its own richer outcome (bought/skipped, reward taken, …) when its
/// behaviour lands.
/// </summary>
public sealed record NodeVisitedOutcome(RunNodeKind Kind) : Outcome;

/// <summary>
/// One unit the run plays to completion before advancing (<c>GAME_LOOP.md §1.2</c>). Battle (a loop-event)
/// and Poké Center recovery (an interaction-event) are the two implemented today; the other node kinds
/// (shop / treasure / mystery / elite / boss) drop in here as Phase 3 bones without the director's loop body
/// changing. An event emits, may await input, and returns an <see cref="Outcome"/> — it <em>never</em>
/// sequences.
/// </summary>
public interface IRunEvent
{
    Task<Outcome> RunAsync(RunContext ctx);
}
