using creaturegame.Attacks;
using creaturegame.Creatures;
using creaturegame.Evolution;
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
/// canonical Gen 1), with a Poké Center pause after every <paramref name="healEveryNBattles"/>-th win. When the
/// player faints the run ends and a single <see cref="RunEnded"/> carries the summary.
///
/// Core stays generation- and data-agnostic via injected seams: <paramref name="enemySupplier"/> builds the
/// scaled foe (the DB concern lives in the web layer), and <paramref name="checkEvolution"/> resolves a
/// pending evolution between encounters (null = the plain chain). (Renamed from <c>BattleRunner</c>: the run
/// loop graduates into the <c>RunDirector</c> that <c>GAME_LOOP.md §6 Q1</c> anticipated.)
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
        IBattleEventEmitter? emitter = null,
        IBattleRules? rules = null,
        IRandomSource? rng = null,
        int healEveryNBattles = 3,
        Func<Creature, Task<EvolutionOutcome?>>? checkEvolution = null,
        Bag? playerBag = null,
        IReadOnlyList<BiomeDefinition>? playableBiomes = null,
        int minEventsPerBiome = 4,
        int maxEventsPerBiome = 6,
        int biomeOptionCount = 3,
        Func<int, IRandomSource, IReadOnlyList<RunNodeKind>>? nodePlanFactory = null,
        Wallet? wallet = null,
        Func<RewardContext, IRandomSource, RewardChoice>? rewardSupplier = null,
        Func<ShopStockContext, IRandomSource, ShopOffer>? shopSupplier = null,
        // A Shop node is only worth visiting if the player can afford something, so a biome's plan only keeps its
        // Shop slots when the wallet is at least this many ₽ when the biome is entered (the moment the route is
        // fixed — ENCOUNTER_DESIGN.md §5). The web layer passes the cheapest stock price; 0 (the default) never
        // gates, so the legacy chain / tests are unchanged.
        int minShopBudget = 0,
        // Roguelite run-balance rules passed straight through to each encounter's Battle (game-balance tuning,
        // not a seam — see Battle's runRules / RunRules). Null keeps the legacy chain / tests on pure Gen-1 XP.
        RunRules? runRules = null,
        // The run's party container (up to six owned creatures; its Lead is the active player). Passed in so the
        // web session owns the same instance the overview/party panel read (Party threading, ENCOUNTER_DESIGN.md
        // §4). Null keeps the legacy shape — a party of one seeded from `player`.
        Party? party = null,
        // The themed-draft acquisition supplier (web-layer policy): rolled after every win, it decides whether to
        // offer a creature (cadence × n% × the fought-only pool) and builds one when it does — the mirror of the
        // reward / shop suppliers on the acquisition side. Null (the default) = no draft, so tests / the legacy
        // chain never offer one and draw no extra RNG.
        Func<DraftContext, IRandomSource, Task<Creature?>>? draftSupplier = null,
        // The boss-catch acquisition supplier (web-layer policy, Phase 4 Stage 2): rolled after a Boss win only —
        // a small n% chance to add the defeated boss to the party (pure upside; the win XP/reward already applied).
        // Builds a party-ready copy of the boss's species when it fires, else null. Null (the default) = no boss
        // catch, so tests / the legacy chain never offer one and draw no extra RNG.
        Func<BossCatchContext, IRandomSource, Task<Creature?>>? bossCatchSupplier = null
    )
    {
        _state = party is not null ? new RunState(party) : new RunState(player);
        _emitter = emitter;
        _playerInput = playerInput;
        _rng = rng;
        _healEveryNBattles = healEveryNBattles;
        // Each biome's route is a randomised length in [min, max] nodes, rolled per biome when the biome is
        // entered (see Apply) — so biomes vary in size and a longer one has more room for impactful nodes
        // (ENCOUNTER_DESIGN.md §7). Clamped so the range is always valid (≥1 node, max ≥ min) however configured.
        _minEventsPerBiome = Math.Max(1, minEventsPerBiome);
        _maxEventsPerBiome = Math.Max(_minEventsPerBiome, maxEventsPerBiome);
        // How each biome's node route is laid out — defaults to the seeded placeholder; injectable so tests pin
        // a deterministic plan and 3c-2 can swap the tuned curve without touching the director.
        _nodePlanFactory = nodePlanFactory ?? DefaultNodePlan;
        // Biome mode kicks in only when the composition layer supplies a non-empty playable set; otherwise the
        // director runs the legacy endless chain (no route choices), so tests/uses without biomes are unchanged.
        _biomeModeActive = playableBiomes is { Count: > 0 };
        _playableBiomes = playableBiomes ?? [];
        _wallet = wallet;
        _minShopBudget = Math.Max(0, minShopBudget);

        // Reward policy (drop rates, rarity curve, item eligibility) is web-layer roguelite tuning, not a battle
        // seam — the core just defines the vocabulary and consumes whatever's injected. No supplier → every
        // reward roll is RewardChoice.None, so callers without one (tests, the legacy chain) are unchanged.
        rewardSupplier ??= (_, _) => RewardChoice.None;

        // Shop stock policy (which items, run-scaled prices) is web-layer roguelite tuning, not a battle seam —
        // same class as the reward supplier. No supplier → every shop rolls ShopOffer.None, so the node resolves
        // as a silent banner (callers without one — tests, the legacy chain — are unchanged).
        shopSupplier ??= (_, _) => ShopOffer.None;

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
                rules,
                playerBag,
                checkEvolution,
                wallet,
                rewardSupplier,
                runRules,
                draftSupplier,
                bossCatchSupplier
            );
        _battleEvent = Battle(EncounterTier.Normal);
        _eliteEvent = Battle(EncounterTier.Elite);
        _bossEvent = Battle(EncounterTier.Boss);

        _recoveryEvent = new RecoveryRunEvent();
        _biomeChoiceEvent = new BiomeChoiceEvent(playableBiomes ?? [], biomeOptionCount, typeChart);
        _leadChoiceEvent = new LeadChoiceEvent();

        // Interaction nodes (ENCOUNTER_DESIGN.md §5): Shop rolls run-scaled stock and runs a spend-gold buy
        // loop against the wallet/bag; Treasure/Mystery roll and apply a reward. All three block on the player's
        // choices (buy/leave, reward pick) so the client raises a modal.
        _shopEvent = new ShopRunEvent(wallet, playerBag, shopSupplier);
        _treasureEvent = new RewardRunEvent(
            RunNodeKind.Treasure,
            wallet,
            playerBag,
            rewardSupplier
        );
        _mysteryEvent = new RewardRunEvent(RunNodeKind.Mystery, wallet, playerBag, rewardSupplier);
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

/// <summary>
/// The battle node (loop-event): build the next foe scaled to run depth, run the <see cref="Battle"/> to a
/// faint, then resolve the post-win consequences — depth++, the level-up evolution offer, and capturing the
/// carried major status for the next encounter. Returns whether the player survived. Evolution stays inside
/// this win resolution rather than as its own node: it is an immediate consequence of <em>this</em> battle's
/// level-up, not an independently sequenced event (<c>GAME_LOOP.md §5</c>).
/// </summary>
internal sealed class BattleRunEvent(
    Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> enemySupplier,
    EncounterTier tier,
    ITypeChart typeChart,
    IBattleInput enemyInput,
    IReadOnlyList<Attack> movePool,
    IBattleRules? rules,
    Bag? playerBag,
    Func<Creature, Task<EvolutionOutcome?>>? checkEvolution,
    Wallet? wallet,
    Func<RewardContext, IRandomSource, RewardChoice> rewardSupplier,
    RunRules? runRules,
    Func<DraftContext, IRandomSource, Task<Creature?>>? draftSupplier,
    Func<BossCatchContext, IRandomSource, Task<Creature?>>? bossCatchSupplier
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var s = ctx.State;
        var player = s.Player;

        // Announce the node so the encounter map can advance its position pin. Elite/Boss always fire (and the
        // client titles a text banner for them, as before); a plain wild node fires only in biome mode — it
        // drives the map pin but the client filters WildBattle out of the text log, so the wild encounter still
        // slides the foe in with no banner. The legacy endless chain (no current biome, no map) stays silent.
        string nodeKind = tier switch
        {
            EncounterTier.Elite => nameof(RunNodeKind.EliteBattle),
            EncounterTier.Boss => nameof(RunNodeKind.BossBattle),
            _ => nameof(RunNodeKind.WildBattle),
        };
        if (tier != EncounterTier.Normal || s.CurrentBiome is not null)
            ctx.Emitter?.Emit(new RunNodeEntered(nodeKind));

        // RunDepth is the progression depth — 0 for the first node, climbing per node traversed (wins +
        // interaction visits; = BattlesWon in the legacy chain). The supplier scales the next foe (BST band,
        // level) to it, themes it to the current biome (null in the legacy chain), and maps this node's
        // EncounterTier to an archetype; see EncounterFactory.CreateEnemyAsync.
        var enemy = await enemySupplier(player, s.RunDepth, s.CurrentBiome, tier);
        // Remember every species faced in this biome — the "fought-only" pool the themed draft may offer from
        // (ENCOUNTER_DESIGN.md §4). Recorded on encounter (win, loss, or flee all count as "faced"); the set is
        // cleared when the next biome is entered. Empty in the legacy chain (no biome), so no draft can fire.
        s.FoughtSpeciesInBiome.Add(enemy.SpeciesId);
        int levelBefore = player.Level;
        var battle = new Battle(
            player,
            enemy,
            typeChart,
            ctx.PlayerInput,
            enemyInput,
            movePool: movePool,
            rules: rules,
            emitter: ctx.Emitter,
            rng: ctx.Rng,
            playerEntryStatus: player.CarriedStatus,
            playerBag: playerBag,
            // Roar/Whirlwind escape a plain wild battle but fail vs the trainer-analog tiers (Elite/Boss).
            escapable: tier == EncounterTier.Normal,
            // Those same trainer-analog tiers (Elite/Boss) are "trainer-owned" for XP — the Gen-1 trainer ×1.5
            // bonus (applied in the seam); a plain wild battle gets none.
            trainerBattle: tier != EncounterTier.Normal,
            runRules: runRules,
            // Party-aware battle (Phase 4 Stage 3): when the lead faints and a bench member is alive, Battle sends
            // in a replacement against this same enemy instead of ending the run. `player` is the party's Lead, so
            // a switch reassigns Party.Lead (⇒ RunState.Player) and the run continues on the survivor.
            playerParty: s.Party
        );
        await battle.StartFightAsync();

        // A forced switch-on-faint (Phase 4 Stage 3) may have changed the active creature mid-battle: Battle
        // reassigns Party.Lead when it sends in a replacement, so the finisher is the *current* lead, not the
        // `player` that started the fight (which may now be fainted on the bench). Re-read it for every post-battle
        // consequence (win/loss, carried status, evolution). When no switch happened, `active` == `player`.
        var active = s.Player;

        // Roar/Whirlwind ended the encounter (a side fled) — neither a win nor a loss. The player survives, so
        // carry its status into the next event and advance the run; no XP/evolution (nothing fainted).
        if (battle.EndedInFlee)
        {
            active.CarriedStatus = CaptureCarriedStatus(active);
            return new FledOutcome(PlayerFled: active.Battle.HasFled);
        }

        // The battle ends when one side faints. With a party, Battle keeps sending in survivors, so reaching here
        // with a fainted active creature means the WHOLE party is down → the run is over (read by the director's
        // while-loop); otherwise it is a win (whoever finished is the active creature).
        if (!active.IsAlive())
            return new BattleOutcome(false);
        s.BattlesWon++;
        await GrantBattleRewardAsync(enemy, s, ctx);

        // Evolution check — Gen 1 attempts evolution on a level-up, so only when this battle actually raised the
        // finisher's level. Gated to the no-switch case: `levelBefore` is the creature that STARTED the battle, so
        // a switched-in finisher (a different creature) can't be compared against it — its evolution is offered on
        // its next clean win instead. A declined evolution re-offers at the next level-up.
        if (ReferenceEquals(active, player) && active.Level > levelBefore)
            await TryEvolveAsync(active, ctx);

        // Default: the finisher's major status carries into its next encounter, stored ON the creature (the
        // multi-creature carry model — each party member keeps its own ailment while benched); a Poké Center heal
        // clears it. The generation decides the out-of-battle form (Gen 1 reverts Toxic to Poison).
        active.CarriedStatus = CaptureCarriedStatus(active);

        // Acquisition (ENCOUNTER_DESIGN.md §4): the last beat of a win, and at most one offer per win. A Boss win
        // routes to the boss-catch channel — a small chance to add the boss you just beat (Stage 2); every other
        // win routes to the themed draft — cadence × n% × the fought-only pool (Stage 1c). Both raise the same
        // reusable blocking AcquisitionOffered (only the source + how the offered creature is chosen differ); each
        // supplier owns its whole policy and returns a built creature only when it fires, else null (the common
        // case). A headless / AI input declines by default, so neither channel stalls the chain or builds a party.
        if (tier == EncounterTier.Boss)
            await OfferBossCatchAsync(enemy, s, ctx);
        else
            await OfferDraftAsync(s, ctx);

        return new BattleOutcome(true);
    }

    // Rolls the themed draft for this win and, if the supplier offers a creature, raises the acquisition offer
    // (blocking; the client shows the modal) and deposits the result into the party. Silent when no supplier is
    // configured (tests / the legacy chain) or the roll declined to offer (null) — no RNG is drawn on a
    // non-cadence win, so the seeded stream only moves when a draft actually rolls.
    private async Task OfferDraftAsync(RunState s, RunContext ctx)
    {
        if (draftSupplier is null)
            return;
        var offered = await draftSupplier(
            new DraftContext(
                s.Player,
                s.RunDepth,
                s.CurrentBiome,
                s.FoughtSpeciesInBiome,
                s.BattlesWon
            ),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        if (offered is null)
            return;
        await AcquisitionResolution.OfferAndDepositAsync(offered, "ThemedDraft", s.Party, ctx);
    }

    // Rolls the boss catch for this Boss win and, if the supplier offers the defeated boss, raises the acquisition
    // offer (blocking; the client shows the modal) and deposits it into the party — the same offer + roster
    // plumbing the draft uses, only the source ("BossCatch") + the single offered option differ. Silent when no
    // supplier is configured (tests / the legacy chain) or the small catch roll declined (null). Only reached on a
    // Boss win, so a plain wild/elite win never draws the catch roll and can't perturb the seeded stream.
    private async Task OfferBossCatchAsync(Creature boss, RunState s, RunContext ctx)
    {
        if (bossCatchSupplier is null)
            return;
        var offered = await bossCatchSupplier(
            new BossCatchContext(boss),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        if (offered is null)
            return;
        await AcquisitionResolution.OfferAndDepositAsync(offered, "BossCatch", s.Party, ctx);
    }

    // Rolls this win's reward and — if anything rolled — offers it as a pick-one-of-N choice (blocking; the
    // client raises the modal), then applies the chosen option. Silent when nothing was rolled
    // (RewardChoice.None is the common case for a wild win — a chance at a drop, not a guarantee; a Boss always
    // rolls). Headless/AI inputs auto-pick option 0, so the chain never stalls.
    private Task GrantBattleRewardAsync(Creature enemy, RunState s, RunContext ctx)
    {
        var choice = rewardSupplier(
            new RewardContext(
                NodeKindForTier(tier),
                enemy.Level,
                s.RunDepth,
                PlayerCondition.From(s.Player)
            ),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        return RewardResolution.OfferAndApplyAsync(choice, "Battle", wallet, playerBag, ctx);
    }

    private static RunNodeKind NodeKindForTier(EncounterTier tier) =>
        tier switch
        {
            EncounterTier.Elite => RunNodeKind.EliteBattle,
            EncounterTier.Boss => RunNodeKind.BossBattle,
            _ => RunNodeKind.WildBattle,
        };

    // Offers, then applies, a pending evolution if the resolver reports one. The player can cancel (Gen 1
    // B-cancel) — the prompt blocks awaiting the decision; on cancel the creature is untouched and re-offered
    // at the next level-up. The from-identity is captured before EvolveTo (which overwrites name/species/stats)
    // so the events carry both forms for the sprite morph.
    private async Task TryEvolveAsync(Creature player, RunContext ctx)
    {
        if (checkEvolution is null)
            return;
        if (await checkEvolution(player) is not { } evolution)
            return;

        string fromName = player.Name;
        int fromSpeciesId = player.SpeciesId;
        var newForm = evolution.NewForm;
        string toName = newForm.Name.ToUpper(); // matches how EvolveTo names the creature

        ctx.Emitter?.Emit(new EvolutionOffered(fromName, toName, fromSpeciesId, newForm.Id));
        bool allow = await ctx.PlayerInput.ConfirmEvolutionAsync(
            new EvolutionPromptContext(player, newForm.Id, toName)
        );
        if (!allow)
        {
            ctx.Emitter?.Emit(new EvolutionCancelled(fromName));
            return;
        }

        player.EvolveTo(newForm);
        player.Learnset = evolution.NewLearnset;

        ctx.Emitter?.Emit(
            new CreatureEvolved(fromName, player.Name, fromSpeciesId, player.SpeciesId)
        );

        // Evolution grants no moves itself, but the evolved form may learn one at the current level.
        await MoveLearning.LearnMovesForLevelAsync(
            player,
            player.Level,
            ctx.Emitter,
            ctx.PlayerInput
        );
    }

    // Major status carries into the next encounter; the generation decides what each status becomes out of
    // battle (Gen 1 reverts Toxic to regular Poison) via IBattleRules.CarryStatusOutOfBattle. Volatile
    // conditions (confusion, stat stages, …) live only in BattleState and are dropped by the per-battle reset
    // — they are never captured here. The sleep counter carries so a sleeping creature keeps counting down.
    private CarriedStatus? CaptureCarriedStatus(Creature c)
    {
        var status = (rules ?? Gen1BattleRules.Instance).CarryStatusOutOfBattle(c.Battle.Status);
        if (status == StatusCondition.None)
            return null;
        int sleepTurns = status == StatusCondition.Sleep ? c.Battle.SleepTurns : 0;
        return new CarriedStatus(status, sleepTurns);
    }
}

/// <summary>
/// The Poké Center node (interaction-event): offer a full restore, then heal or leave the player as-is per the
/// choice (interactive input blocks on the accept/skip; automated inputs accept by default, so the chain still
/// heals headless/in tests). Accepting cures HP/PP/status — so nothing carries; declining keeps the carried
/// status the battle event captured.
/// </summary>
internal sealed class RecoveryRunEvent : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var s = ctx.State;
        var player = s.Player;

        ctx.Emitter?.Emit(new RecoveryOffered(player.Name, player.SpeciesId, s.BattlesWon));
        bool accept = await ctx.PlayerInput.ConfirmRecoveryAsync(
            new RecoveryContext(player, s.BattlesWon)
        );
        if (accept)
        {
            // A Poké Center restores the WHOLE party, not just the lead — benched members keep permanent HP
            // across biomes, so this tops every owned creature back up (HP/PP/status). FullHeal also clears each
            // member's own persisted CarriedStatus (the multi-creature carry model), so nothing carries onward.
            foreach (var member in s.Party.Members)
                member.FullHeal();
            ctx.Emitter?.Emit(new PlayerRecovered(player.Name, player.Attributes.HP));
            // The lead-only PlayerRecovered above can't carry the bench's restored HP — so push a fresh party
            // snapshot too (the 1a/1b deferral: whole-party heal is state-correct, this makes the benched
            // members' heal visible on the wire for the party panel). A no-op-looking single-member party still
            // keeps the panel in lockstep.
            ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(s.Party)));
        }
        else
        {
            ctx.Emitter?.Emit(new RecoveryDeclined(player.Name));
        }

        return new RecoveryOutcome(accept);
    }
}

/// <summary>
/// The between-biome lead choice (interaction-event, Phase 4 Stage 1d): at a biome boundary — after the Poké
/// Center, before the route choice — offer the party and let the player pick which member leads into the next
/// biome. Only reached when the party holds more than one creature (the director gates it). Reassigns
/// <see cref="Creatures.Party.Lead"/> (⇒ <see cref="RunState.Player"/>) when the pick differs from the current
/// lead; keeping the current lead (or a stale / out-of-range pick) is a no-op. Automated / AI inputs keep the
/// current lead via the <see cref="IBattleInput.ChooseLeadAsync"/> default, so a headless run never stalls.
/// <para>Touches nothing in the battle engine — this is a between-biome choice, not in-battle switching. A swap
/// never touches either creature's <see cref="Creature.CarriedStatus"/>: the outgoing lead keeps whatever it is
/// carrying while it benches (still ailed if the preceding Poké Center was declined), and the incoming lead
/// enters on its own carried status. Nothing transfers between them, so the previous lead's status can never leak
/// onto the switch-in.</para>
/// </summary>
internal sealed class LeadChoiceEvent : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var party = ctx.State.Party;

        ctx.Emitter?.Emit(new LeadChoiceOffered(PartyProjection.Snapshot(party)));
        int index = await ctx.PlayerInput.ChooseLeadAsync(new LeadChoiceContext(party));

        // Apply only a real change: an in-range index that isn't already the lead. Keeping the current lead or a
        // stale / out-of-range pick leaves the roster untouched and emits nothing (a pure no-op).
        if (index >= 0 && index < party.Count && index != party.LeadIndex)
        {
            party.SetLead(index);
            // No status reconciliation needed: under the multi-creature carry model each creature carries its own
            // out-of-battle status (Creature.CarriedStatus), so the incoming lead enters on its own status and the
            // outgoing lead keeps its ailment while benched. The next battle sources playerEntryStatus from the
            // new lead directly, so the previous lead's status can never leak onto the switch-in.
            ctx.Emitter?.Emit(new LeadChanged(party.Lead.Name, party.Lead.SpeciesId));
            ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(party)));
        }

        return new LeadChoiceOutcome();
    }
}

/// <summary>
/// The route-choice node (interaction-event): offer the next leg of the run and await the player's pick. At run
/// start the options are any playable biome; afterwards they are the current biome's playable neighbours — so
/// the player charts a route through the authored biome graph (<c>ENCOUNTER_DESIGN.md §1</c>). The offered set
/// (and its order) is sampled on the run RNG, so a seed reproduces the same map. The chosen biome becomes the
/// theme for the next stretch of encounters via <see cref="RunState.CurrentBiome"/>.
/// <para>
/// The <em>opening</em> route choice is biased: it guarantees at least one offered biome is a favourable
/// matchup for the starter (a biome whose theme the player's type(s) hit super-effectively, per
/// <paramref name="typeChart"/>), so a run never opens with only unfavourable lanes. The bias applies only to
/// the first choice and only when such a biome exists in the pool; everything else is the plain seeded sample.
/// </para>
/// </summary>
internal sealed class BiomeChoiceEvent(
    IReadOnlyList<BiomeDefinition> playable,
    int optionCount,
    ITypeChart typeChart
) : IRunEvent
{
    private readonly Dictionary<string, BiomeDefinition> _byId = playable.ToDictionary(b => b.Id);

    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var options = PickOptions(ctx.State.CurrentBiome, ctx.State.Player, ctx.Rng);

        ctx.Emitter?.Emit(
            new BiomeChoiceOffered(
                options.Select(b => new BiomeOption(b.Id, b.Name, b.Types)).ToList()
            )
        );
        string chosenId = await ctx.PlayerInput.ChooseBiomeAsync(new BiomeChoiceContext(options));
        // An unknown id (stale / malformed pick) falls back to the first offered biome — mirrors the move-slot
        // fallback; the route is never left unset.
        var chosen = options.FirstOrDefault(b => b.Id == chosenId) ?? options[0];

        ctx.Emitter?.Emit(new BiomeEntered(chosen.Id, chosen.Name, chosen.Types));
        return new BiomeChoiceOutcome(chosen);
    }

    // The biomes to offer: at run start (no current biome) any playable biome; otherwise the current biome's
    // playable neighbours (charting a route through the authored graph). A dead-end with no playable neighbours
    // falls back to the whole playable set so the run never stalls. Up to optionCount, sampled on the run RNG.
    private IReadOnlyList<BiomeDefinition> PickOptions(
        BiomeDefinition? current,
        Creature player,
        IRandomSource? rng
    )
    {
        var r = rng ?? SystemRandomSource.Instance;
        IReadOnlyList<BiomeDefinition> pool = current is null
            ? playable
            : current.Neighbours.Where(_byId.ContainsKey).Select(id => _byId[id]).ToList();
        if (pool.Count == 0)
            pool = playable;

        // Opening choice only (no biome entered yet): guarantee a favourable lane. Skipped when every biome
        // would be offered anyway (pool ≤ offer) or the starter has no super-effective coverage at all (e.g. a
        // pure Normal type) — both fall through to the plain sample below.
        if (current is null && pool.Count > optionCount)
            return SampleEnsuringFavourableMatchup(pool, optionCount, player, r);

        return Sample(pool, optionCount, r);
    }

    // Like Sample, but reserves one biome the starter is strong into so the opening offer always has a viable
    // lane. Reserve a random favourable biome, fill the rest from the pool, then shuffle so the guaranteed pick
    // isn't always slot 0. Every draw is on the run RNG, so the offer still replays from the seed. Falls back to
    // a plain sample when no biome qualifies (the starter has no super-effective coverage in this pool).
    private IReadOnlyList<BiomeDefinition> SampleEnsuringFavourableMatchup(
        IReadOnlyList<BiomeDefinition> pool,
        int k,
        Creature player,
        IRandomSource rng
    )
    {
        var favourable = pool.Where(b => IsFavourableMatchup(player, b)).ToList();
        if (favourable.Count == 0)
            return Sample(pool, k, rng);

        var reserved = favourable[rng.Next(favourable.Count)];
        var rest = pool.Where(b => b.Id != reserved.Id).ToList();
        var result = new List<BiomeDefinition>(k) { reserved };
        result.AddRange(Sample(rest, k - 1, rng));
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

    // A biome is a favourable opener if any of the starter's types hits any of the biome's theme types
    // super-effectively (>1×) per the active type chart (the generation seam) — its STAB lands hard on that
    // biome's on-theme foes. Reads the chart, never a hardcoded matchup, so it stays gen-correct.
    private bool IsFavourableMatchup(Creature player, BiomeDefinition biome)
    {
        foreach (var atk in PlayerAttackTypes(player))
        foreach (var def in biome.Types)
            if (typeChart.GetMultiplier(atk, def) > 1.0)
                return true;
        return false;
    }

    private static IEnumerable<DamageType> PlayerAttackTypes(Creature player)
    {
        if (player.Type1 is { } t1)
            yield return t1;
        if (player.Type2 is { } t2)
            yield return t2;
    }

    // Up to k items in a seed-reproducible random order (partial Fisher–Yates over a copy), so the offered set
    // and its order replay from the run seed.
    private static IReadOnlyList<BiomeDefinition> Sample(
        IReadOnlyList<BiomeDefinition> pool,
        int k,
        IRandomSource rng
    )
    {
        var copy = pool.ToList();
        int take = Math.Min(k, copy.Count);
        for (int i = 0; i < take; i++)
        {
            int j = i + rng.Next(copy.Count - i);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy.GetRange(0, take);
    }
}

/// <summary>
/// The Shop node (interaction-event): announce the node, roll this visit's run-scaled stock (the web-layer
/// <c>ShopCalculator</c> policy), then run a spend-gold <em>buy loop</em> — offer the stock, and repeatedly take
/// the player's buy/leave choice, charging the <see cref="Wallet"/> and adding to the <see cref="Bag"/> on each
/// affordable purchase — until the player leaves. Unlike the one-shot reward/biome prompts, a shop is iterative
/// (buy several, then go). No stock rolled (<see cref="ShopOffer.None"/> — no supplier / empty catalog) → the
/// node resolves as a silent banner. Headless / AI inputs leave immediately via the
/// <see cref="IBattleInput.ChooseShopActionAsync"/> default, so a run never stalls on the shop.
/// </summary>
internal sealed class ShopRunEvent(
    Wallet? wallet,
    Bag? playerBag,
    Func<ShopStockContext, IRandomSource, ShopOffer> shopSupplier
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        ctx.Emitter?.Emit(new RunNodeEntered(RunNodeKind.Shop.ToString()));

        var offer = shopSupplier(
            new ShopStockContext(ctx.State.RunDepth),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        if (offer.IsEmpty)
            return new NodeVisitedOutcome(RunNodeKind.Shop); // no stock → the banner is the whole node

        int Balance() => wallet?.Balance ?? 0;
        ctx.Emitter?.Emit(new ShopOffered(offer.Items, Balance()));

        // The buy loop: keep taking buy/leave choices until the player leaves. A buy charges the wallet and adds
        // to the bag only if affordable; a stale / out-of-range / unaffordable index is a no-op (the client's
        // balance is already correct, so no event is needed), so the loop simply re-prompts.
        while (
            await ctx.PlayerInput.ChooseShopActionAsync(new ShopContext(offer.Items, Balance()))
                is BuyShopItem buy
        )
        {
            if (buy.Index < 0 || buy.Index >= offer.Items.Count)
                continue;

            var item = offer.Items[buy.Index];
            // Refuse a buy that would exceed the Gen 1 99-per-slot ceiling — check before charging, so the
            // wallet is never spent on a clamped no-op.
            if (playerBag is not null && playerBag.IsFull(item.ItemId))
                continue;
            if (wallet is null || !wallet.TrySpend(item.Price))
                continue; // can't afford (or no wallet) → nothing bought, re-prompt

            playerBag?.Add(item.ItemId);
            ctx.Emitter?.Emit(new ShopItemPurchased(item.ItemName, item.Price, wallet.Balance));
        }

        return new NodeVisitedOutcome(RunNodeKind.Shop);
    }
}

/// <summary>
/// The Treasure/Mystery node (interaction-event): announce the node, roll its reward (guaranteed for Treasure,
/// a wildcard for Mystery — the web-layer <c>RewardCalculator</c> policy), then offer it as a pick-one-of-N
/// choice the player resolves before advancing the biome (the "open a chest" beat the client raises a modal
/// for). No reward supplier configured → the roll is <see cref="RewardChoice.None"/> and the node resolves
/// silently (an empty Mystery is itself a valid outcome).
/// </summary>
internal sealed class RewardRunEvent(
    RunNodeKind kind,
    Wallet? wallet,
    Bag? playerBag,
    Func<RewardContext, IRandomSource, RewardChoice> rewardSupplier
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        ctx.Emitter?.Emit(new RunNodeEntered(kind.ToString()));

        var choice = rewardSupplier(
            new RewardContext(
                kind,
                EnemyLevel: 0,
                ctx.State.RunDepth,
                PlayerCondition.From(ctx.State.Player)
            ),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        await RewardResolution.OfferAndApplyAsync(choice, kind.ToString(), wallet, playerBag, ctx);

        return new NodeVisitedOutcome(kind);
    }
}

/// <summary>
/// Shared reward-choice resolution used by every reward-earning node (battle win, Treasure, Mystery): if the
/// roll produced a choice, offer it — a blocking pick-one-of-N the client raises the modal for — clamp the
/// picked index, apply the chosen option (gold → wallet, item → bag), and announce it with a
/// <see cref="RewardGranted"/> (so the HUD/log render exactly as before, now driven by the <em>chosen</em>
/// option). An empty roll (<see cref="RewardChoice.None"/>) is silent. Headless / AI inputs auto-pick option 0
/// via the <see cref="IBattleInput.ChooseRewardAsync"/> default, so a run never stalls on the modal.
/// </summary>
internal static class RewardResolution
{
    public static async Task OfferAndApplyAsync(
        RewardChoice choice,
        string source,
        Wallet? wallet,
        Bag? playerBag,
        RunContext ctx
    )
    {
        if (choice.IsEmpty)
            return;

        ctx.Emitter?.Emit(new RewardChoiceOffered(source, choice.Options));
        int index = await ctx.PlayerInput.ChooseRewardAsync(
            new RewardChoiceContext(source, choice.Options)
        );
        // Tolerate a stale / malformed pick — fall back to the first option (mirrors the biome-choice fallback),
        // so the run is never left unresolved on an out-of-range index.
        if (index < 0 || index >= choice.Options.Count)
            index = 0;

        int gold = 0;
        var itemNames = new List<string>();
        switch (choice.Options[index])
        {
            case GoldRewardOption g:
                gold = g.Gold;
                wallet?.Credit(gold);
                break;
            case ItemRewardOption item:
                playerBag?.Add(item.ItemId);
                itemNames.Add(item.ItemName);
                break;
            case HealRewardOption heal:
                ApplyHeal(heal, ctx.State.Player, ctx.Emitter);
                itemNames.Add(heal.Label);
                break;
        }

        ctx.Emitter?.Emit(new RewardGranted(source, gold, wallet?.Balance ?? gold, itemNames));
    }

    // Applies a pre-resolved quick-heal to the player's creature on the spot — but only the components the option
    // carries (the web policy set each flag from what the creature actually needed): restore some HP, cure any
    // status, and — when RestoreLowPp is set — top EVERY non-full move back to max (Elixir-style, matching
    // PpRestoreItemEffect's all-moves precedent), not only the move that tripped the low-PP threshold. Reuses the
    // same gen-invariant primitives + events as item use (Healed / StatusCleared / PpRestored), so the client's
    // timeline renders it exactly like a potion/status-cure.
    internal static void ApplyHeal(
        HealRewardOption heal,
        Creature player,
        IBattleEventEmitter? emitter
    )
    {
        if (heal.HpRestore > 0 && player.Attributes.HP < player.Attributes.MaxHP)
        {
            int before = player.Attributes.HP;
            player.Attributes.ReceiveHealing(heal.HpRestore); // caps at MaxHP
            emitter?.Emit(
                new Healed(player.Name, player.Attributes.HP - before, player.Attributes.HP)
            );
        }

        if (heal.CureStatus && player.Battle.Status != StatusCondition.None)
            HealingItemEffect.ClearStatus(player, emitter);

        if (heal.RestoreLowPp)
        {
            foreach (var move in player.MoveSet)
            {
                if (move.PowerPointsCurrent < move.Base.PowerPointsMax)
                {
                    move.PowerPointsCurrent = move.Base.PowerPointsMax;
                    emitter?.Emit(
                        new PpRestored(player.Name, move.Base.Name ?? "", move.PowerPointsCurrent)
                    );
                }
            }
        }
    }
}

/// <summary>
/// Shared acquisition-offer resolution used by every acquisition channel (themed draft and boss catch):
/// raise the blocking offer — the client shows the modal, so the player accepts / declines / (on a full party)
/// picks a member to swap out — then deposit the result into the <see cref="Party"/> and announce it. A
/// <em>decline</em> (the automated / AI default) is a pure sequencing no-op: the roster is unchanged and only an
/// <see cref="AcquisitionDeclined"/> line is emitted. An accept a full party can't honour (no valid replace slot)
/// falls back to a decline, so a stale pick never strands the run. On a deposit a <see cref="CreatureAcquired"/>
/// plus a fresh <see cref="PartyUpdated"/> snapshot follow. Both channels reuse this — only the offered creature
/// and the <paramref name="source"/> label differ.
/// </summary>
internal static class AcquisitionResolution
{
    public static async Task OfferAndDepositAsync(
        Creature offered,
        string source,
        Party party,
        RunContext ctx
    )
    {
        ctx.Emitter?.Emit(
            new AcquisitionOffered(
                source,
                offered.SpeciesId,
                offered.Name,
                offered.Level,
                CreatureTypes(offered),
                offered.Attributes.MaxHP,
                party.IsFull,
                PartyProjection.Snapshot(party)
            )
        );

        var decision = await ctx.PlayerInput.ChooseAcquisitionAsync(
            new AcquisitionContext(offered, party, source)
        );

        if (!decision.Accept)
        {
            ctx.Emitter?.Emit(new AcquisitionDeclined(offered.Name));
            return;
        }

        if (party.IsFull)
        {
            // Full roster → the accept must name a bench slot to swap out; an out-of-range / missing slot (a
            // stale pick) is tolerated as a decline rather than stranding the run. The lead slot is refused too:
            // swapping the active creature mid-chain is a lead change, which is Stage 1d's between-biome
            // ChooseLeadAsync flow — this offer must never reassign the lead (the client hides it as a target,
            // and the server enforces the same rule so a malformed / regressed client can't slip it through).
            if (
                decision.ReplaceSlot is not int slot
                || slot < 0
                || slot >= party.Count
                || slot == party.LeadIndex
            )
            {
                ctx.Emitter?.Emit(new AcquisitionDeclined(offered.Name));
                return;
            }
            string replacedName = party.Members[slot].Name;
            party.Replace(slot, offered);
            ctx.Emitter?.Emit(
                new CreatureAcquired(offered.Name, offered.SpeciesId, true, replacedName)
            );
        }
        else
        {
            party.Add(offered);
            ctx.Emitter?.Emit(new CreatureAcquired(offered.Name, offered.SpeciesId, false, null));
        }

        ctx.Emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(party)));
    }

    private static IReadOnlyList<DamageType> CreatureTypes(Creature c)
    {
        var types = new List<DamageType>(2);
        if (c.Type1 is { } t1)
            types.Add(t1);
        if (c.Type2 is { } t2)
            types.Add(t2);
        return types;
    }
}
