using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// Drives a run as a logic-sequenced chain of events (<c>GAME_LOOP.md §3</c>): <see cref="ChooseNextEvent"/>
/// picks the next <see cref="IRunEvent"/> purely from <see cref="RunState"/>, the event resolves to an
/// <see cref="Outcome"/>, and the outcome feeds back into run state. It is the <em>single owner of sequence</em>
/// — the player only changes an event's outcome, never the order — so new node kinds (shop / treasure /
/// mystery / elite / boss) drop in by branching <see cref="ChooseNextEvent"/>, with the loop body untouched.
///
/// Today the chain is the endless run: a wild <see cref="Battle"/> per encounter (one persistent player whose
/// permanent half — HP, PP, XP, Level — carries across; each battle resets the transient half at its start,
/// canonical Gen 1), with a Poké Center pause after every <see cref="RunDirectorOptions.HealEveryNBattles"/>-th
/// win. When the player faints the run ends and a single <see cref="RunEnded"/> carries the summary.
///
/// Core stays generation- and data-agnostic via injected seams: <paramref name="enemySupplier"/> builds the
/// scaled foe (the DB concern lives in the web layer), and the rest of the injected policy —
/// <see cref="RunDirectorOptions.CheckEvolution"/>, the reward / shop / acquisition suppliers, the biome set —
/// arrives on <see cref="RunDirectorOptions"/>; omit it entirely for the plain chain. (Renamed from
/// <c>BattleRunner</c>: the run loop graduates into the <c>RunDirector</c> that <c>GAME_LOOP.md §6 Q1</c>
/// anticipated.)
/// </summary>
public sealed class RunDirector
{
    private readonly RunState _state;
    private readonly IBattleEventEmitter? _emitter;
    private readonly IBattleInput _playerInput;
    private readonly IRandomSource? _rng;
    private readonly int _healEveryNBattles;
    private readonly int _minEventsPerBiome;
    private readonly int _maxEventsPerBiome;
    private readonly bool _biomeModeActive;
    private readonly IReadOnlyList<BiomeDefinition> _playableBiomes;
    private readonly Wallet? _wallet;
    private readonly int _minShopBudget;
    private readonly Func<int, IRandomSource, IReadOnlyList<RunNodeKind>> _nodePlanFactory;
    private readonly IRunEvent _battleEvent;
    private readonly IRunEvent _eliteEvent;
    private readonly IRunEvent _bossEvent;
    private readonly IRunEvent _recoveryEvent;
    private readonly IRunEvent _biomeChoiceEvent;
    private readonly IRunEvent _leadChoiceEvent;
    private readonly IRunEvent _shopEvent;
    private readonly IRunEvent _treasureEvent;
    private readonly IRunEvent _mysteryEvent;

    public RunDirector(
        Creature player,
        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> enemySupplier,
        ITypeChart typeChart,
        IBattleInput playerInput,
        IBattleInput enemyInput,
        IReadOnlyList<Attack> movePool,
        RunDirectorOptions? options = null
    )
    {
        // Every knob and injected policy supplier lives on the options record; omitting it is the legacy endless
        // chain. See RunDirectorOptions for what each one means and what its absence implies.
        var o = options ?? new RunDirectorOptions();

        _state = o.Party is not null ? new RunState(o.Party) : new RunState(player);
        _emitter = o.Emitter;
        _playerInput = playerInput;
        _rng = o.Rng;
        _healEveryNBattles = o.HealEveryNBattles;
        // Each biome's route is a randomised length in [min, max] nodes, rolled per biome when the biome is
        // entered (see Apply) — so biomes vary in size and a longer one has more room for impactful nodes
        // (ENCOUNTER_DESIGN.md §7). Clamped so the range is always valid (≥1 node, max ≥ min) however configured.
        _minEventsPerBiome = Math.Max(1, o.MinEventsPerBiome);
        _maxEventsPerBiome = Math.Max(_minEventsPerBiome, o.MaxEventsPerBiome);
        _nodePlanFactory = o.NodePlanFactory ?? DefaultNodePlan;
        // Biome mode kicks in only when the composition layer supplies a non-empty playable set; otherwise the
        // director runs the legacy endless chain (no route choices), so tests/uses without biomes are unchanged.
        _biomeModeActive = o.PlayableBiomes is { Count: > 0 };
        _playableBiomes = o.PlayableBiomes ?? [];
        _wallet = o.Wallet;
        _minShopBudget = Math.Max(0, o.MinShopBudget);

        var rewardSupplier = o.RewardSupplier ?? ((_, _) => RewardChoice.None);
        var shopSupplier = o.ShopSupplier ?? ((_, _) => ShopOffer.None);

        // The three battle nodes differ only by the EncounterTier they hand the supplier (which the web layer
        // maps to an IEnemyArchetype): WildBattle ≈ today's Medium, Elite/Boss climb. Same collaborators
        // otherwise. Elite/Boss also emit a node banner before the fight.
        BattleRunEvent Battle(EncounterTier tier) =>
            new(
                enemySupplier,
                tier,
                typeChart,
                enemyInput,
                movePool,
                o.Rules,
                o.PlayerBag,
                o.CheckEvolution,
                o.Wallet,
                rewardSupplier,
                o.RunRules,
                o.DraftSupplier,
                o.BossCatchSupplier
            );
        _battleEvent = Battle(EncounterTier.Normal);
        _eliteEvent = Battle(EncounterTier.Elite);
        _bossEvent = Battle(EncounterTier.Boss);

        _recoveryEvent = new RecoveryRunEvent();
        _biomeChoiceEvent = new BiomeChoiceEvent(_playableBiomes, o.BiomeOptionCount, typeChart);
        _leadChoiceEvent = new LeadChoiceEvent();

        // Interaction nodes (ENCOUNTER_DESIGN.md §5): Shop rolls run-scaled stock and runs a spend-gold buy
        // loop against the wallet/bag; Treasure/Mystery roll and apply a reward. All three block on the player's
        // choices (buy/leave, reward pick) so the client raises a modal.
        _shopEvent = new ShopRunEvent(o.Wallet, o.PlayerBag, shopSupplier);
        _treasureEvent = new RewardRunEvent(
            RunNodeKind.Treasure,
            o.Wallet,
            o.PlayerBag,
            rewardSupplier
        );
        _mysteryEvent = new RewardRunEvent(
            RunNodeKind.Mystery,
            o.Wallet,
            o.PlayerBag,
            rewardSupplier
        );
    }

    /// <summary>The live run state — exposed <c>internal</c> as a test seam so a test can assert run-state
    /// invariants (e.g. the fought-only pool accumulating per biome and resetting on a biome change) after a
    /// controlled run. Not part of the public surface; production code never reads it from outside.</summary>
    internal RunState State => _state;

    public async Task RunAsync()
    {
        var ctx = new RunContext(_state, _emitter, _playerInput, _rng);

        // Reveal the whole playable region graph once up front so the client can draw the encounter map (a
        // presentation signal — the run's route is still charted one BiomeChoice at a time). Neighbours are
        // filtered to the playable subset so the client never references a biome it wasn't sent.
        if (_biomeModeActive)
            _emitter?.Emit(BuildRegionMap());

        while (_state.Player.IsAlive())
        {
            var next = ChooseNextEvent(_state);
            var outcome = await next.RunAsync(ctx);
            Apply(_state, outcome);
        }

        _emitter?.Emit(new RunEnded(_state.BattlesWon, _state.Player.Level, _state.Player.Name));
    }

    /// <summary>
    /// The single decider of sequence — pure over run state (<c>GAME_LOOP.md §3/§4</c>). In biome mode: chart a
    /// route at the start of each biome, run that biome's encounters, then a Poké Center caps it before the next
    /// choice. Legacy (no biomes): a Poké Center after every Nth win, otherwise the next encounter. Future node
    /// kinds branch here; the loop body never reads run structure.
    /// </summary>
    private IRunEvent ChooseNextEvent(RunState s)
    {
        if (_biomeModeActive)
        {
            if (s.NeedsBiomeChoice)
            {
                // Between-biome lead choice (Stage 1d): a one-shot prompt owed at this boundary, ahead of the
                // route choice, when the party has more than one creature. Gated so it fires exactly once (the
                // event clears LeadChoicePending via its outcome). A lone-starter party never sees it.
                if (s.LeadChoicePending && s.Party.Count > 1)
                    return _leadChoiceEvent;
                return _biomeChoiceEvent; // run start / post-Center: pick the next biome
            }
            if (s.EventsInCurrentBiome >= s.BiomeNodePlan.Count)
                return _recoveryEvent; // biome's nodes cleared → its Poké Center cap
            return EventForNode(s.BiomeNodePlan[s.EventsInCurrentBiome]); // the planned node
        }

        return _healEveryNBattles > 0 && s.BattlesWon / _healEveryNBattles > s.RecoveriesDone
            ? _recoveryEvent
            : _battleEvent;
    }

    // The event that runs one planned node kind. The three battle tiers and three interaction bones drop in
    // here; the loop body never branches on node kind (GAME_LOOP.md §3).
    private IRunEvent EventForNode(RunNodeKind kind) =>
        kind switch
        {
            RunNodeKind.WildBattle => _battleEvent,
            RunNodeKind.EliteBattle => _eliteEvent,
            RunNodeKind.BossBattle => _bossEvent,
            RunNodeKind.Shop => _shopEvent,
            RunNodeKind.Treasure => _treasureEvent,
            RunNodeKind.Mystery => _mysteryEvent,
            // Every kind has an explicit arm; throw rather than silently route a future unmapped kind to a
            // wild battle (which would mask the missing arm). Caught at the first test run.
            _ => throw new InvalidOperationException($"No run event mapped for RunNodeKind {kind}"),
        };

    // Folds an outcome back into run state, the only channel from a (player-influenced) outcome to future
    // sequencing. A battle event already advanced BattlesWon / CarriedStatus itself, and a loss is read by the
    // while-loop's IsAlive() guard — so this records the route choice, the per-biome progress, and the recovery
    // milestone / biome boundary.
    private void Apply(RunState s, Outcome outcome)
    {
        switch (outcome)
        {
            case BiomeChoiceOutcome biome:
                s.CurrentBiome = biome.Chosen;
                s.EventsInCurrentBiome = 0;
                // A fresh biome starts with an empty fought-only pool — the themed draft only ever offers
                // species faced in the biome you're currently in (ENCOUNTER_DESIGN.md §4).
                s.FoughtSpeciesInBiome.Clear();
                // Roll this biome's length (4–6 by default), then lay out its route now (seeded → reproducible):
                // interior nodes then the Boss apex. Both the length and the node mix draw from the run RNG, so
                // the same seed reproduces the same biome size and contents.
                var planRng = _rng ?? SystemRandomSource.Instance;
                int length = planRng.Next(_minEventsPerBiome, _maxEventsPerBiome + 1); // +1 → max inclusive
                s.BiomeNodePlan = GateShopsByBudget(_nodePlanFactory(length, planRng));
                s.NeedsBiomeChoice = false;
                // Reveal the rolled ladder so the encounter map can draw this biome's nodes ahead of time. Pure
                // presentation — the plan is already fixed for the biome, so emitting it changes no sequencing.
                _emitter?.Emit(
                    new BiomeNodePlanRevealed(s.BiomeNodePlan.Select(k => k.ToString()).ToList())
                );
                break;
            case BattleOutcome { Won: true }:
                s.RunDepth++; // progression depth (= BattlesWon in legacy; biome mode adds interaction nodes too)
                if (_biomeModeActive)
                    s.EventsInCurrentBiome++; // node resolved → advance the biome (Poké Center caps the plan)
                break;
            case NodeVisitedOutcome:
                if (_biomeModeActive)
                {
                    s.RunDepth++; // an interaction node consumes a biome slot AND advances the depth axis
                    s.EventsInCurrentBiome++;
                }
                break;
            case FledOutcome:
                // A flee consumes the encounter node (so it doesn't repeat) and advances the depth axis, but
                // is not a win (BattlesWon unchanged) and awards no XP. The player survived → the run continues.
                s.RunDepth++;
                if (_biomeModeActive)
                    s.EventsInCurrentBiome++;
                break;
            case RecoveryOutcome:
                s.RecoveriesDone++; // legacy milestone bookkeeping
                if (_biomeModeActive)
                {
                    // Biome done; keep CurrentBiome so its neighbours are the next options, then re-choose.
                    s.NeedsBiomeChoice = true;
                    // Owe a between-biome lead choice before that route choice iff the party has grown past the
                    // lone starter (Stage 1d). Decided here (the boundary) — the party can't change between the
                    // Poké Center and the route choice, so this reads the final roster for the boundary.
                    s.LeadChoicePending = s.Party.Count > 1;
                }
                break;
            case LeadChoiceOutcome:
                // The lead reassignment (if any) was applied inside the event; just clear the gate so the next
                // event is the route choice.
                s.LeadChoicePending = false;
                break;
        }
    }

    // Projects the playable biome subset into the region-map payload the client draws: each biome with its type
    // theme and the ids of its neighbours that are *also* in the playable subset (the graph edges). Filtering
    // neighbours to the subset means the client never gets an edge to a biome it wasn't sent.
    private RegionMapRevealed BuildRegionMap()
    {
        var playableIds = _playableBiomes.Select(b => b.Id).ToHashSet();
        return new RegionMapRevealed(
            _playableBiomes
                .Select(b => new RegionMapBiome(
                    b.Id,
                    b.Name,
                    b.Types,
                    b.Neighbours.Where(playableIds.Contains).ToList(),
                    b.MapX,
                    b.MapY
                ))
                .ToList()
        );
    }

    // A Shop is a dead node when the player can't afford anything, so a biome only keeps its Shop slots if the
    // wallet clears the minimum stock price at biome entry (the moment the route is fixed — the player's
    // "encounters possible" are decided then). Below budget, swap this biome's Shop slots for wild battles. The
    // node-plan roll's RNG draw already happened, so only the node *kind* changes — the run's seeded stream (and
    // the rest of the plan) is untouched. A no-op when unconfigured (_minShopBudget 0) or the wallet clears it.
    private IReadOnlyList<RunNodeKind> GateShopsByBudget(IReadOnlyList<RunNodeKind> plan)
    {
        if (_minShopBudget <= 0 || (_wallet?.Balance ?? 0) >= _minShopBudget)
            return plan;
        if (!plan.Contains(RunNodeKind.Shop))
            return plan;
        return plan.Select(k => k == RunNodeKind.Shop ? RunNodeKind.WildBattle : k).ToList();
    }

    /// <summary>
    /// The default biome route layout: <paramref name="length"/> nodes, the last always the Boss (the themed
    /// apex — <c>ENCOUNTER_DESIGN.md §4</c>), each interior slot rolled independently from the weighted table in
    /// <see cref="PickInteriorNode"/> (battle-heavy, with elites the step-up and the no-op feature bones rare).
    /// Seeded on <paramref name="rng"/> → reproducible. Public + injectable via the director's
    /// <c>nodePlanFactory</c> so a run can supply a different layout without touching the director.
    /// </summary>
    public static IReadOnlyList<RunNodeKind> DefaultNodePlan(int length, IRandomSource rng)
    {
        if (length <= 1)
            return [RunNodeKind.BossBattle];

        var plan = new RunNodeKind[length];
        for (int i = 0; i < length - 1; i++)
            plan[i] = PickInteriorNode(rng);
        plan[length - 1] = RunNodeKind.BossBattle;
        return plan;
    }

    // Interior-node weights (sum 100). Battle-heavy — the run is a battle game, so most slots are encounters and
    // Elites are the intra-biome step-up before the Boss. The interaction nodes (shop/treasure/mystery) stay a
    // minority of slots so they punctuate rather than dilute the combat; Treasure (a player-positive reward)
    // leads them, Mystery (the wildcard) trails. Independent roll per slot (the chosen 3c-2 model).
    private static RunNodeKind PickInteriorNode(IRandomSource rng) =>
        rng.Next(100) switch
        {
            < 70 => RunNodeKind.WildBattle, // 70
            < 88 => RunNodeKind.EliteBattle, // 18
            < 94 => RunNodeKind.Treasure, // 6
            < 98 => RunNodeKind.Shop, // 4
            _ => RunNodeKind.Mystery, // 2
        };
}
