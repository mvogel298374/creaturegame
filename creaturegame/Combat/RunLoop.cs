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
public sealed class RunState
{
    /// <summary>
    /// The run's party — up to six owned creatures, one of which is the active <see cref="Player"/> (the lead).
    /// The acquisition channels (<c>ENCOUNTER_DESIGN.md §4</c>) fill it; the between-biome lead choice picks
    /// which member battles next. A single-creature run is just a party of one (the legacy shape).
    /// </summary>
    public Party Party { get; }

    /// <summary>Constructs a run around an existing <paramref name="party"/> (its lead is the active creature).</summary>
    public RunState(Party party)
    {
        Party = party;
    }

    /// <summary>Convenience: start a run with a single-creature party seeded from the <paramref name="player"/>
    /// starter — the legacy shape used by the endless chain and most tests.</summary>
    public RunState(Creature player)
        : this(new Party(player)) { }

    /// <summary>The active creature the run is played with — the party's <see cref="Creatures.Party.Lead"/>; its
    /// permanent half carries across events. Changing the lead (between biomes) changes what this returns.</summary>
    public Creature Player => Party.Lead;

    /// <summary>
    /// The distinct species ids the player has faced in the <em>current</em> biome — the "fought-only"
    /// acquisition pool (<c>ENCOUNTER_DESIGN.md §4</c>): the themed draft may only offer a species from this set,
    /// so a run can never draft something it never fought. Populated as each encounter is built and cleared when
    /// a new biome is entered (a fresh biome starts with an empty fought pool). Empty in the legacy chain.
    /// </summary>
    public HashSet<int> FoughtSpeciesInBiome { get; } = [];

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

    /// <summary>
    /// True when a between-biome lead choice is owed before the next route choice (Phase 4 Stage 1d) — set at a
    /// biome boundary (after the Poké Center) when the party holds more than one creature, and cleared once the
    /// lead choice resolves. Gates the one-shot <c>ChooseLeadAsync</c> prompt so it fires exactly once per
    /// boundary, ahead of the <c>BiomeChoiceEvent</c>. Never set at run start (the party is the lone starter).
    /// </summary>
    public bool LeadChoicePending { get; set; }
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

/// <summary>A "quick heal" option — a potion-style heal applied on the spot to the player's creature, restoring
/// only the components that currently apply. The magnitude/components are pre-resolved by the web-layer reward
/// policy (like the other options' amounts), so the core applies them deterministically. Offered
/// <em>smart-randomly</em> — only when the creature has something to heal, biased toward how badly it needs it.
/// <see cref="HpRestore"/> is an absolute HP amount (0 = no HP component); <see cref="CureStatus"/> clears any
/// major status; <see cref="RestoreLowPp"/>, when set, tops <em>every</em> non-full move back to max
/// (Elixir-style — set by the policy when any move is below its low-PP threshold). <see cref="Label"/> is the
/// display name for the choice card.</summary>
public sealed record HealRewardOption(
    int HpRestore,
    bool CureStatus,
    bool RestoreLowPp,
    string Label
) : RewardOption;

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
public sealed record RewardContext(
    RunNodeKind Source,
    int EnemyLevel,
    int Depth,
    PlayerCondition? Condition = null
);

/// <summary>A lightweight, generation-agnostic snapshot of the player creature's current condition, handed to
/// the reward supplier so it can offer a context-sensitive <see cref="HealRewardOption"/> (more likely, and
/// sized, when the creature is hurt / statused / low on PP). Null when a caller doesn't supply it (e.g. tests),
/// in which case the policy simply never offers a heal. <see cref="LowestPpFraction"/> is the minimum
/// current/max PP ratio across the creature's moves (1.0 when every move is full or it has no moves).</summary>
public sealed record PlayerCondition(
    int CurrentHp,
    int MaxHp,
    bool HasStatus,
    double LowestPpFraction
)
{
    /// <summary>Snapshots the given creature's current condition for a reward roll.</summary>
    public static PlayerCondition From(Creature c)
    {
        double lowestPp = 1.0;
        foreach (var move in c.MoveSet)
        {
            int max = move.Base.PowerPointsMax;
            if (max > 0)
                lowestPp = Math.Min(lowestPp, (double)move.PowerPointsCurrent / max);
        }
        return new PlayerCondition(
            c.Attributes.HP,
            c.Attributes.MaxHP,
            c.Battle.Status != StatusCondition.None,
            lowestPp
        );
    }
}

// --- Acquisition (party draft) -----------------------------------------------------------------------------

/// <summary>What a themed-draft roll needs to know about the moment it fires, handed to the injected draft
/// supplier (the mirror of <see cref="RewardContext"/> / <see cref="ShopStockContext"/> on the acquisition side).
/// The supplier decides — from all of these — whether to offer a creature this win and, if so, builds one: the
/// <see cref="Lead"/> and <see cref="Depth"/> scale it (BST target / level), <see cref="Biome"/> is the current
/// theme, and <see cref="FoughtSpecies"/> is the fought-only pool it must draw from (never an un-fought species —
/// <c>ENCOUNTER_DESIGN.md §4</c>). <see cref="BattlesWon"/> drives the cadence gate. Returns null (no offer) on
/// any gate miss — the common case, exactly like a no-drop reward roll.</summary>
public sealed record DraftContext(
    Creature Lead,
    int Depth,
    BiomeDefinition? Biome,
    IReadOnlyCollection<int> FoughtSpecies,
    int BattlesWon
);

// --- Shop (spend-gold purchase node) -----------------------------------------------------------------------

/// <summary>One item on offer in a Shop node's stock: the resolved item id + display name, its run-scaled
/// <see cref="Price"/> in ₽, and the <see cref="RewardRarity"/> it rolled at (for the client's card colour).
/// Ids/names/prices are pre-resolved by the injected shop supplier (the web-layer <c>ShopCalculator</c>), so the
/// core never touches the item catalog — the mirror of <see cref="ItemRewardOption"/> on the spend side.</summary>
public sealed record ShopOfferItem(int ItemId, string ItemName, int Price, RewardRarity Rarity);

/// <summary>A Shop node's stock: the items the player may buy this visit. Infinite quantity while affordable
/// (Gen-1 style — re-buyable until the wallet runs out). <see cref="None"/> means no stock rolled (no supplier
/// configured / empty catalog) — the node resolves silently, the mirror of <see cref="RewardChoice.None"/>.</summary>
public sealed record ShopOffer(IReadOnlyList<ShopOfferItem> Items)
{
    public static readonly ShopOffer None = new([]);

    public bool IsEmpty => Items.Count == 0;
}

/// <summary>What a shop-stock roll needs to know about the moment it fires, handed to the injected shop supplier
/// (mirrors <see cref="RewardContext"/>): the run's progression <see cref="Depth"/>, which the web-layer policy
/// scales stock rarity / price to.</summary>
public sealed record ShopStockContext(int Depth);

/// <summary>The player's choice each step of a Shop visit: buy the stock item at <see cref="BuyShopItem.Index"/>,
/// or <see cref="LeaveShop"/> to end the visit. The shop event loops on this until the player leaves.</summary>
public abstract record ShopAction;

/// <summary>Buy the stock item at this index (into the offered <see cref="ShopOffer.Items"/>). An out-of-range
/// or unaffordable index is a no-op downstream, so a stale pick never strands the run.</summary>
public sealed record BuyShopItem(int Index) : ShopAction;

/// <summary>End the shop visit — the run advances to the next node. Singleton (no state).</summary>
public sealed record LeaveShop : ShopAction
{
    public static readonly LeaveShop Instance = new();
}

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

/// <summary>Outcome of a between-biome lead choice: the lead reassignment (if any) was already applied inside the
/// event; this only carries the sequencing fact that the choice resolved, so the director clears the
/// <see cref="RunState.LeadChoicePending"/> gate and proceeds to the route choice.</summary>
public sealed record LeadChoiceOutcome : Outcome;

/// <summary>
/// Outcome of an interaction node (Shop / Treasure / Mystery): records which kind was visited so the director
/// advances the biome. The node's own effect (items bought, reward taken) is applied inside the event against
/// the wallet/bag; this outcome only carries the sequencing fact (a node was resolved), not its economic result.
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
