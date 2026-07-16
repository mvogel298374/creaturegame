using creaturegame.Creatures;
using creaturegame.Evolution;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// The optional surface of <see cref="RunDirector"/> — every knob and injected policy supplier that has a
/// working default, gathered off the constructor's parameter list.
///
/// The <em>injection</em> pattern here is deliberate and stays: run-layer policy (drop rates, shop stock,
/// acquisition odds, the node curve) is web-layer roguelite tuning, not a battle seam, so the core defines the
/// vocabulary and consumes whatever's handed to it (<c>GAME_LOOP.md</c> / <c>ENCOUNTER_DESIGN.md</c>). Only the
/// delivery mechanism moved: a new node kind or acquisition channel now adds a property here instead of another
/// positional parameter + another default on a signature that had reached 25.
///
/// Every property defaults to the same value the old parameter did, so <c>new RunDirector(…)</c> with no options
/// is the legacy endless chain: no biomes, no wallet, no rewards/shops/acquisition, pure Gen-1 XP.
/// </summary>
public sealed record RunDirectorOptions
{
    /// <summary>Where run events are published. Null = a headless run (tests, the legacy chain).</summary>
    public IBattleEventEmitter? Emitter { get; init; }

    /// <summary>The generation-variable battle rules handed to each encounter's <see cref="Battle"/>.</summary>
    public IBattleRules? Rules { get; init; }

    /// <summary>The run's single seeded RNG — threaded through every nondeterministic step so a run replays
    /// from its seed.</summary>
    public IRandomSource? Rng { get; init; }

    /// <summary>Legacy-chain only: a Poké Center pause after every Nth win. Ignored in biome mode, where the
    /// Center caps each biome instead.</summary>
    public int HealEveryNBattles { get; init; } = 3;

    /// <summary>Resolves a pending evolution between encounters (the DB concern lives in the web layer).
    /// Null = the plain chain, no evolution.</summary>
    public Func<Creature, Task<EvolutionOutcome?>>? CheckEvolution { get; init; }

    /// <summary>The run's bag, threaded into every encounter's player side; consumed items stay gone.</summary>
    public Bag? PlayerBag { get; init; }

    /// <summary>The run's playable biome subset. A non-empty set flips the director from the legacy endless
    /// chain to biome traversal (<c>ENCOUNTER_DESIGN.md §7</c>).</summary>
    public IReadOnlyList<BiomeDefinition>? PlayableBiomes { get; init; }

    /// <summary>Lower bound of a biome's randomised route length, rolled per biome on entry.</summary>
    public int MinEventsPerBiome { get; init; } = 4;

    /// <summary>Upper bound of a biome's randomised route length. Clamped to at least
    /// <see cref="MinEventsPerBiome"/> however configured.</summary>
    public int MaxEventsPerBiome { get; init; } = 6;

    /// <summary>How many biomes are offered at each between-biome route choice.</summary>
    public int BiomeOptionCount { get; init; } = 3;

    /// <summary>How a biome's node route is laid out. Injectable so tests pin a deterministic plan and the
    /// tuned curve can be swapped without touching the director. Null = the seeded default plan.</summary>
    public Func<int, IRandomSource, IReadOnlyList<RunNodeKind>>? NodePlanFactory { get; init; }

    /// <summary>The run's gold. Null = no economy (the legacy chain).</summary>
    public Wallet? Wallet { get; init; }

    /// <summary>Reward policy (drop rates, rarity curve, item eligibility) — web-layer tuning. Null = every
    /// reward roll is <see cref="RewardChoice.None"/>.</summary>
    public Func<RewardContext, IRandomSource, RewardChoice>? RewardSupplier { get; init; }

    /// <summary>Shop stock policy (which items, run-scaled prices) — web-layer tuning, same class as
    /// <see cref="RewardSupplier"/>. Null = every shop rolls <see cref="ShopOffer.None"/> (a silent banner).</summary>
    public Func<ShopStockContext, IRandomSource, ShopOffer>? ShopSupplier { get; init; }

    /// <summary>A Shop node is only worth visiting if the player can afford something, so a biome's plan only
    /// keeps its Shop slots when the wallet is at least this many ₽ when the biome is entered (the moment the
    /// route is fixed — <c>ENCOUNTER_DESIGN.md §5</c>). The web layer passes the cheapest stock price; 0 (the
    /// default) never gates.</summary>
    public int MinShopBudget { get; init; }

    /// <summary>Roguelite run-balance rules passed straight through to each encounter's <see cref="Battle"/>
    /// (game-balance tuning, not a seam). Null keeps the run on pure Gen-1 XP.</summary>
    public RunRules? RunRules { get; init; }

    /// <summary>The run's party container (up to six owned creatures; its Lead is the active player). Passed in
    /// so the web session owns the same instance the overview/party panel read. Null = the legacy shape, a party
    /// of one seeded from the player.</summary>
    public Party? Party { get; init; }

    /// <summary>The themed-draft acquisition supplier (web-layer policy): rolled after every win, it decides
    /// whether to offer a creature (cadence × n% × the fought-only pool) and builds one when it does. Null = no
    /// draft, so tests / the legacy chain never offer one and draw no extra RNG.</summary>
    public Func<DraftContext, IRandomSource, Task<Creature?>>? DraftSupplier { get; init; }

    /// <summary>The boss-catch acquisition supplier (web-layer policy): rolled after a Boss win only — a small
    /// n% chance to add the defeated boss to the party (pure upside; the win XP/reward already applied). Null =
    /// no boss catch, so tests / the legacy chain never offer one and draw no extra RNG.</summary>
    public Func<BossCatchContext, IRandomSource, Task<Creature?>>? BossCatchSupplier { get; init; }
}
