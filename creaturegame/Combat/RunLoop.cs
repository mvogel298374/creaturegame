using creaturegame.Creatures;

namespace creaturegame.Combat;

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

    /// <summary>Encounters won so far — the run "depth". Climbs on each battle win and scales the next foe.</summary>
    public int BattlesWon { get; set; }

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

    /// <summary>Battles won inside the current biome — drives the per-biome length (the Poké Center caps it).</summary>
    public int EventsInCurrentBiome { get; set; }

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

/// <summary>
/// The typed result of an event, which the <see cref="RunDirector"/> reads to advance the run
/// (<c>GAME_LOOP.md §3</c>). An event emits narration and may await input, then returns an <c>Outcome</c>; it
/// never decides what happens next. New event kinds add their own outcome record.
/// </summary>
public abstract record Outcome;

/// <summary>Outcome of a battle event: whether the player survived. A loss ends the run.</summary>
public sealed record BattleOutcome(bool Won) : Outcome;

/// <summary>Outcome of a Poké Center recovery event: whether the player accepted the heal.</summary>
public sealed record RecoveryOutcome(bool Healed) : Outcome;

/// <summary>Outcome of a route-choice event: the biome the player elected to enter next.</summary>
public sealed record BiomeChoiceOutcome(BiomeDefinition Chosen) : Outcome;

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
