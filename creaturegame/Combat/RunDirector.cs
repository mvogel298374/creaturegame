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
    private readonly IRunEvent _battleEvent;
    private readonly IRunEvent _recoveryEvent;

    public RunDirector(
        Creature player,
        Func<Creature, int, Task<Creature>> enemySupplier,
        ITypeChart typeChart,
        IBattleInput playerInput,
        IBattleInput enemyInput,
        IReadOnlyList<Attack> movePool,
        IBattleEventEmitter? emitter = null,
        IBattleRules? rules = null,
        IRandomSource? rng = null,
        int healEveryNBattles = 3,
        Func<Creature, Task<EvolutionOutcome?>>? checkEvolution = null,
        Bag? playerBag = null
    )
    {
        _state = new RunState(player);
        _emitter = emitter;
        _playerInput = playerInput;
        _rng = rng;
        _healEveryNBattles = healEveryNBattles;
        _battleEvent = new BattleRunEvent(
            enemySupplier,
            typeChart,
            enemyInput,
            movePool,
            rules,
            playerBag,
            checkEvolution
        );
        _recoveryEvent = new RecoveryRunEvent();
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
    /// The single decider of sequence — pure over run state (<c>GAME_LOOP.md §3/§4</c>). Today: a Poké Center
    /// pause after every Nth win (exactly once per milestone, via <see cref="RunState.RecoveriesDone"/>),
    /// otherwise the next encounter. Future node kinds branch here; the loop body never reads run structure.
    /// </summary>
    private IRunEvent ChooseNextEvent(RunState s) =>
        _healEveryNBattles > 0 && s.BattlesWon / _healEveryNBattles > s.RecoveriesDone
            ? _recoveryEvent
            : _battleEvent;

    // Folds an outcome back into run state, the only channel from a (player-influenced) outcome to future
    // sequencing. A battle event already advanced BattlesWon / CarriedStatus itself, and a loss is read by the
    // while-loop's IsAlive() guard — so only the recovery milestone needs recording here.
    private static void Apply(RunState s, Outcome outcome)
    {
        if (outcome is RecoveryOutcome)
            s.RecoveriesDone++; // milestone consumed whether the player healed or declined
    }
}

/// <summary>
/// The battle node (loop-event): build the next foe scaled to run depth, run the <see cref="Battle"/> to a
/// faint, then resolve the post-win consequences — depth++, the level-up evolution offer, and capturing the
/// carried major status for the next encounter. Returns whether the player survived. Evolution stays inside
/// this win resolution rather than as its own node: it is an immediate consequence of <em>this</em> battle's
/// level-up, not an independently sequenced event (<c>GAME_LOOP.md §5</c>).
/// </summary>
internal sealed class BattleRunEvent(
    Func<Creature, int, Task<Creature>> enemySupplier,
    ITypeChart typeChart,
    IBattleInput enemyInput,
    IReadOnlyList<Attack> movePool,
    IBattleRules? rules,
    Bag? playerBag,
    Func<Creature, Task<EvolutionOutcome?>>? checkEvolution
) : IRunEvent
{
    public async Task<Outcome> RunAsync(RunContext ctx)
    {
        var s = ctx.State;
        var player = s.Player;

        // BattlesWon is the run depth — 0 for the first encounter, climbing each win. The supplier scales the
        // next foe (BST band, level, tier) to it; see EncounterFactory.CreateEnemyAsync.
        var enemy = await enemySupplier(player, s.BattlesWon);
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
            playerBag: playerBag
        );
        await battle.StartFightAsync();

        // The battle ends when one side faints. If the player dropped, the run is over (read by the director's
        // while-loop); otherwise it is a win.
        if (!player.IsAlive())
            return new BattleOutcome(false);
        s.BattlesWon++;

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
