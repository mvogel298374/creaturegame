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
    private readonly Func<int, IRandomSource, IReadOnlyList<RunNodeKind>> _nodePlanFactory;
    private readonly IRunEvent _battleEvent;
    private readonly IRunEvent _eliteEvent;
    private readonly IRunEvent _bossEvent;
    private readonly IRunEvent _recoveryEvent;
    private readonly IRunEvent _biomeChoiceEvent;
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
        Func<RewardContext, IRandomSource, RunReward>? rewardSupplier = null,
        // Roguelite run-balance rules passed straight through to each encounter's Battle (game-balance tuning,
        // not a seam — see Battle's runRules / RunRules). Null keeps the legacy chain / tests on pure Gen-1 XP.
        RunRules? runRules = null
    )
    {
        _state = new RunState(player);
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

        // Reward policy (drop rates, gold curve, item eligibility) is web-layer roguelite tuning, not a battle
        // seam — the core just defines the vocabulary and consumes whatever's injected. No supplier → every
        // reward roll is RunReward.Empty, so callers without one (tests, the legacy chain) are unchanged.
        rewardSupplier ??= (_, _) => RunReward.Empty;

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
                runRules
            );
        _battleEvent = Battle(EncounterTier.Normal);
        _eliteEvent = Battle(EncounterTier.Elite);
        _bossEvent = Battle(EncounterTier.Boss);

        _recoveryEvent = new RecoveryRunEvent();
        _biomeChoiceEvent = new BiomeChoiceEvent(playableBiomes ?? [], biomeOptionCount, typeChart);

        // Interaction-node bones (ENCOUNTER_DESIGN.md §5): Shop is still a no-op banner (deferred to an
        // immediate follow-up — needs a spend-gold purchase modal); Treasure/Mystery now roll and apply a
        // reward, blocking on the player's acknowledgement.
        _shopEvent = new InteractionStubEvent(RunNodeKind.Shop);
        _treasureEvent = new RewardRunEvent(
            RunNodeKind.Treasure,
            wallet,
            playerBag,
            rewardSupplier
        );
        _mysteryEvent = new RewardRunEvent(RunNodeKind.Mystery, wallet, playerBag, rewardSupplier);
    }

    public async Task RunAsync()
    {
        var ctx = new RunContext(_state, _emitter, _playerInput, _rng);
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
                return _biomeChoiceEvent; // run start / post-Center: pick the next biome
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
                // Roll this biome's length (4–6 by default), then lay out its route now (seeded → reproducible):
                // interior nodes then the Boss apex. Both the length and the node mix draw from the run RNG, so
                // the same seed reproduces the same biome size and contents.
                var planRng = _rng ?? SystemRandomSource.Instance;
                int length = planRng.Next(_minEventsPerBiome, _maxEventsPerBiome + 1); // +1 → max inclusive
                s.BiomeNodePlan = _nodePlanFactory(length, planRng);
                s.NeedsBiomeChoice = false;
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
                    // Biome done; keep CurrentBiome so its neighbours are the next options, then re-choose.
                    s.NeedsBiomeChoice = true;
                break;
        }
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
    // Elites are the intra-biome step-up before the Boss. The feature bones (shop/treasure/mystery) are rare
    // while they're still no-op banners; Treasure (a player-positive reward) leads them, Mystery (the wildcard)
    // trails. Independent roll per slot (the chosen 3c-2 model); raise the feature weights as their behaviour lands.
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
    Func<RewardContext, IRandomSource, RunReward> rewardSupplier,
    RunRules? runRules
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var s = ctx.State;
        var player = s.Player;

        // Elite/Boss nodes announce themselves before the fight (a wild battle just slides the foe in, as today).
        string? bannerKind = tier switch
        {
            EncounterTier.Elite => nameof(RunNodeKind.EliteBattle),
            EncounterTier.Boss => nameof(RunNodeKind.BossBattle),
            _ => null,
        };
        if (bannerKind is not null)
            ctx.Emitter?.Emit(new RunNodeEntered(bannerKind));

        // RunDepth is the progression depth — 0 for the first node, climbing per node traversed (wins +
        // interaction visits; = BattlesWon in the legacy chain). The supplier scales the next foe (BST band,
        // level) to it, themes it to the current biome (null in the legacy chain), and maps this node's
        // EncounterTier to an archetype; see EncounterFactory.CreateEnemyAsync.
        var enemy = await enemySupplier(player, s.RunDepth, s.CurrentBiome, tier);
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
            playerEntryStatus: s.CarriedStatus,
            playerBag: playerBag,
            // Roar/Whirlwind escape a plain wild battle but fail vs the trainer-analog tiers (Elite/Boss).
            escapable: tier == EncounterTier.Normal,
            // Those same trainer-analog tiers (Elite/Boss) are "trainer-owned" for XP — the Gen-1 trainer ×1.5
            // bonus (applied in the seam); a plain wild battle gets none.
            trainerBattle: tier != EncounterTier.Normal,
            runRules: runRules
        );
        await battle.StartFightAsync();

        // Roar/Whirlwind ended the encounter (a side fled) — neither a win nor a loss. The player survives, so
        // carry its status into the next event and advance the run; no XP/evolution (nothing fainted).
        if (battle.EndedInFlee)
        {
            s.CarriedStatus = CaptureCarriedStatus(player);
            return new FledOutcome(PlayerFled: player.Battle.HasFled);
        }

        // The battle ends when one side faints. If the player dropped, the run is over (read by the director's
        // while-loop); otherwise it is a win.
        if (!player.IsAlive())
            return new BattleOutcome(false);
        s.BattlesWon++;
        GrantBattleReward(enemy, s, ctx);

        // Evolution check — Gen 1 attempts evolution on a level-up, so only when this battle actually raised
        // the player's level (a declined evolution re-offers at the next level-up, not every win). The battle
        // has already applied the level-ups, so the level is current.
        if (player.Level > levelBefore)
            await TryEvolveAsync(player, ctx);

        // Default: the player's major status carries into the next encounter (a Poké Center heal clears it on
        // the next event); the generation decides the out-of-battle form (Gen 1 reverts Toxic to Poison).
        s.CarriedStatus = CaptureCarriedStatus(player);
        return new BattleOutcome(true);
    }

    // Rolls and applies this win's reward — a battle drop is inline (no ack; the gold/item bump rides the
    // normal log), unlike the blocking Treasure/Mystery reward. Silent when nothing was rolled (RunReward.Empty
    // is the common case — a battle win is a *chance* at a drop, not a guarantee).
    private void GrantBattleReward(Creature enemy, RunState s, RunContext ctx)
    {
        var reward = rewardSupplier(
            new RewardContext(NodeKindForTier(tier), enemy.Level, s.RunDepth),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        if (reward.Gold <= 0 && reward.Items.Count == 0)
            return;

        wallet?.Credit(reward.Gold);
        foreach (var item in reward.Items)
            playerBag?.Add(item.ItemId);

        ctx.Emitter?.Emit(
            new RewardGranted(
                "Battle",
                reward.Gold,
                wallet?.Balance ?? reward.Gold,
                reward.Items.Select(i => i.ItemName).ToList()
            )
        );
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
            player.FullHeal();
            s.CarriedStatus = null;
            ctx.Emitter?.Emit(new PlayerRecovered(player.Name, player.Attributes.HP));
        }
        else
        {
            ctx.Emitter?.Emit(new RecoveryDeclined(player.Name));
        }

        return new RecoveryOutcome(accept);
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
/// A node-kind bone (interaction-event) for Shop / Treasure / Mystery (<c>ENCOUNTER_DESIGN.md §5</c>): announce
/// the node with a <see cref="RunNodeEntered"/> banner and resolve immediately, no behaviour yet. It satisfies
/// the <see cref="IRunEvent"/> contract (emits, returns a <see cref="NodeVisitedOutcome"/> the director folds
/// in to advance the biome) so the node kind is reachable and sequenced now; each graduates to its own event
/// (shop economy, reward, event card) when its behaviour lands — the loop body never changes (<c>GAME_LOOP.md
/// §3</c>).
/// </summary>
internal sealed class InteractionStubEvent(RunNodeKind kind) : IRunEvent
{
    public Task<Outcome> RunAsync(RunContext ctx)
    {
        ctx.Emitter?.Emit(new RunNodeEntered(kind.ToString()));
        return Task.FromResult<Outcome>(new NodeVisitedOutcome(kind));
    }
}

/// <summary>
/// The Treasure/Mystery node (interaction-event): announce the node, roll and apply its reward (guaranteed for
/// Treasure, a wildcard for Mystery — the web-layer <c>RewardCalculator</c> policy), then block on the player's
/// acknowledgement before advancing the biome (unlike a battle-win reward, which is inline/non-blocking — these
/// two are the "open a chest" beat the client raises a modal for). No reward supplier configured → the roll is
/// <see cref="RunReward.Empty"/> and the ack still fires (an empty Mystery is itself a valid outcome).
/// </summary>
internal sealed class RewardRunEvent(
    RunNodeKind kind,
    Wallet? wallet,
    Bag? playerBag,
    Func<RewardContext, IRandomSource, RunReward> rewardSupplier
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        ctx.Emitter?.Emit(new RunNodeEntered(kind.ToString()));

        var reward = rewardSupplier(
            new RewardContext(kind, EnemyLevel: 0, ctx.State.RunDepth),
            ctx.Rng ?? SystemRandomSource.Instance
        );
        wallet?.Credit(reward.Gold);
        foreach (var item in reward.Items)
            playerBag?.Add(item.ItemId);

        var itemNames = reward.Items.Select(i => i.ItemName).ToList();
        ctx.Emitter?.Emit(
            new RewardGranted(
                kind.ToString(),
                reward.Gold,
                wallet?.Balance ?? reward.Gold,
                itemNames
            )
        );
        await ctx.PlayerInput.AcknowledgeRewardAsync(
            new RewardAckContext(kind.ToString(), reward.Gold, itemNames)
        );

        return new NodeVisitedOutcome(kind);
    }
}
