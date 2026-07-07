using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;

namespace creaturegame.Tests.TestSupport;

/// <summary>
/// Deterministic <see cref="IBattleInput"/> driven by a script of move <i>names</i>. One entry is
/// consumed per <see cref="ChooseMoveAsync"/> call (Battle only consults the input on a free-choice
/// turn — lock-in continuations bypass it), so the script lists only the turns the side actually
/// chooses. When the script runs out it repeats the last entry (so "spam move X" is a single entry).
/// Throws if a scripted move isn't in the creature's moveset — a loud failure beats silently picking
/// something else and masking a bug.
/// </summary>
public sealed class ScriptedInput(params string[] moveNames) : IBattleInput
{
    private readonly Queue<string> _script = new(moveNames);
    private string? _last;
    private int? _forgetSlot;
    private bool _acceptRecovery = true;
    private int _rewardPick;

    /// <summary>
    /// The fixed answer to a level-up replace-move prompt: a slot index (0–3) to forget, or <c>null</c> to
    /// decline (the default — same as the interface default, so an un-configured input never learns on a
    /// full moveset). Returned by <see cref="ChooseMoveToForgetAsync"/>.
    /// </summary>
    public ScriptedInput ForgetsSlot(int? slot)
    {
        _forgetSlot = slot;
        return this;
    }

    public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
    {
        string name = _script.Count > 0 ? _script.Dequeue() : _last ?? "";
        _last = name;

        var move =
            context.Attacker.MoveSet.FirstOrDefault(m => m.Base.Name == name)
            ?? throw new InvalidOperationException(
                $"ScriptedInput: '{context.Attacker.Name}' has no move named '{name}' "
                    + $"(moveset: {string.Join(", ", context.Attacker.MoveSet.Select(m => m.Base.Name))})."
            );
        return Task.FromResult(move);
    }

    public Task<int?> ChooseMoveToForgetAsync(MoveReplacementContext context) =>
        Task.FromResult(_forgetSlot);

    /// <summary>Makes this input skip the between-encounter Poké Center recovery (default is to accept, same as
    /// the interface default). Returned by <see cref="ConfirmRecoveryAsync"/>.</summary>
    public ScriptedInput DeclinesRecovery()
    {
        _acceptRecovery = false;
        return this;
    }

    public Task<bool> ConfirmRecoveryAsync(RecoveryContext context) =>
        Task.FromResult(_acceptRecovery);

    /// <summary>Sets which reward-choice option this input picks (the index returned by
    /// <see cref="ChooseRewardAsync"/>); default 0 (the first option). An out-of-range pick is clamped to the
    /// first option by the run loop, so a test can safely over-shoot.</summary>
    public ScriptedInput PicksReward(int index)
    {
        _rewardPick = index;
        return this;
    }

    /// <summary>Reward-choice offers this input has received, in order — lets a test prove a reward-earning node
    /// actually offered a pick-one-of-N (and inspect the options presented).</summary>
    public List<RewardChoiceContext> RewardChoicesOffered { get; } = [];

    public Task<int> ChooseRewardAsync(RewardChoiceContext context)
    {
        RewardChoicesOffered.Add(context);
        return Task.FromResult(_rewardPick);
    }
}

/// <summary>
/// A fully configurable <see cref="IBattleRules"/> double for interaction tests: every roll defaults
/// to real Gen 1 behaviour, and a fluent setter pins just the ones a test needs to control. Combine
/// with a seeded RNG on the <see cref="BattleScenario"/> for full determinism. The common baseline —
/// always hit, no crit, no damage variance — is one call (<see cref="Deterministic"/>).
/// </summary>
public sealed class ScriptableRules : DelegatingBattleRules
{
    /// <summary>Forwards an optional seeded RNG to the base, so this double's unpinned rolls (anything not
    /// overridden below) draw reproducibly from that seed instead of the global RNG.</summary>
    public ScriptableRules(IRandomSource? rng = null)
        : base(rng) { }

    private bool _alwaysHit;
    private bool _noCrit;
    private bool _alwaysCrit;
    private bool _noVariance;
    private bool _forceSecondary;
    private int? _sleepTurns;
    private int? _confusionTurns;
    private int? _rampageTurns;
    private int? _bideTurns;
    private int? _bindingTurns;
    private int? _disableTurns;
    private int? _multiHitCount;

    /// <summary>Always hit, never crit, no damage variance — the usual deterministic-damage baseline.</summary>
    public ScriptableRules Deterministic()
    {
        _alwaysHit = true;
        _noCrit = true;
        _noVariance = true;
        return this;
    }

    public ScriptableRules AlwaysHit()
    {
        _alwaysHit = true;
        return this;
    }

    public ScriptableRules NoCrit()
    {
        _noCrit = true;
        return this;
    }

    public ScriptableRules AlwaysCrit()
    {
        _alwaysCrit = true;
        return this;
    }

    public ScriptableRules NoVariance()
    {
        _noVariance = true;
        return this;
    }

    /// <summary>Force every secondary effect (status/stat-drop/flinch chance) to land.</summary>
    public ScriptableRules ForceSecondary()
    {
        _forceSecondary = true;
        return this;
    }

    public ScriptableRules SleepTurns(int n)
    {
        _sleepTurns = n;
        return this;
    }

    public ScriptableRules ConfusionTurns(int n)
    {
        _confusionTurns = n;
        return this;
    }

    public ScriptableRules RampageTurns(int n)
    {
        _rampageTurns = n;
        return this;
    }

    public ScriptableRules BideTurns(int n)
    {
        _bideTurns = n;
        return this;
    }

    public ScriptableRules BindingTurns(int n)
    {
        _bindingTurns = n;
        return this;
    }

    public ScriptableRules DisableTurns(int n)
    {
        _disableTurns = n;
        return this;
    }

    public ScriptableRules MultiHits(int n)
    {
        _multiHitCount = n;
        return this;
    }

    public override int GetHitThreshold(int acc, int accStage, int evaStage) =>
        _alwaysHit ? 256 : base.GetHitThreshold(acc, accStage, evaStage);

    public override double GetCritChance(Creature a, Attack m) =>
        _alwaysCrit ? 1.0
        : _noCrit ? 0.0
        : base.GetCritChance(a, m);

    public override double RollDamageVariance() => _noVariance ? 1.0 : base.RollDamageVariance();

    public override int GetSecondaryEffectChance(Attack m, SecondaryEffectKind e) =>
        _forceSecondary ? 100 : base.GetSecondaryEffectChance(m, e);

    public override int RollSleepTurns() => _sleepTurns ?? base.RollSleepTurns();

    public override int RollConfusionTurns() => _confusionTurns ?? base.RollConfusionTurns();

    public override int RollRampageTurns() => _rampageTurns ?? base.RollRampageTurns();

    public override int RollBideTurns() => _bideTurns ?? base.RollBideTurns();

    public override int RollBindingTurns() => _bindingTurns ?? base.RollBindingTurns();

    public override int RollDisableTurns() => _disableTurns ?? base.RollDisableTurns();

    public override int RollMultiHitCount() => _multiHitCount ?? base.RollMultiHitCount();
}

/// <summary>
/// "Run a whole battle with scripted moves and inspect everything that happened." The full-
/// <see cref="Battle"/> analog of <see cref="MoveScenario"/>: set up both creatures, script each
/// side's move sequence by name, pin the rules and RNG seed, then <see cref="RunAsync"/> and assert
/// over the recorded event stream and the final creature state. Built for hunting bugs in mechanic
/// interactions across turns.
/// </summary>
public sealed class BattleScenario
{
    private Creature _player = TestCreatures.Make("Player");
    private Creature _enemy = TestCreatures.Make("Enemy");
    private string[] _playerScript = [];
    private string[] _enemyScript = [];
    private IBattleRules? _rules; // null → default rules sharing the run's seed (built in RunAsync)
    private int _seed;
    private int? _playerForgetSlot;
    private bool _escapable = true; // wild battle by default; false = the Elite/Boss trainer analog

    public BattleScenario Player(Creature c)
    {
        _player = c;
        return this;
    }

    /// <summary>The player's answer to a level-up replace-move prompt: slot 0–3 to forget, or null to decline
    /// (the default). Lets a flow test drive the full-moveset learn path deterministically.</summary>
    public BattleScenario PlayerForgetsSlot(int? slot)
    {
        _playerForgetSlot = slot;
        return this;
    }

    public BattleScenario Enemy(Creature c)
    {
        _enemy = c;
        return this;
    }

    /// <summary>The move names the player chooses, one per free-choice turn (last repeats).</summary>
    public BattleScenario PlayerUses(params string[] moveNames)
    {
        _playerScript = moveNames;
        return this;
    }

    /// <summary>The move names the enemy chooses, one per free-choice turn (last repeats).</summary>
    public BattleScenario EnemyUses(params string[] moveNames)
    {
        _enemyScript = moveNames;
        return this;
    }

    public BattleScenario Rules(IBattleRules rules)
    {
        _rules = rules;
        return this;
    }

    public BattleScenario Seed(int seed)
    {
        _seed = seed;
        return this;
    }

    /// <summary>Whether the battle can be fled — Roar/Whirlwind (ForceFlee) end an escapable wild battle but
    /// fail in a non-escapable one (the Elite/Boss trainer analog). Defaults to true (a plain wild battle).</summary>
    public BattleScenario Escapable(bool escapable)
    {
        _escapable = escapable;
        return this;
    }

    public async Task<BattleScenarioResult> RunAsync()
    {
        var emitter = new RecordingEmitter();
        // Both the battle and (unless a test overrides them) its rules draw from a seeded source, so EVERY
        // roll — including the rules' unpinned Roll* draws — is reproducible from the seed, not only the ones a
        // test explicitly pins. The default rules get their own SeededRandomSource(_seed) rather than sharing
        // the battle's instance, so the battle's own seeded draw order (tie-break, accuracy, crit) is identical
        // to before this wiring and existing seeded scenarios don't shift.
        var rules = _rules ?? new ScriptableRules(new SeededRandomSource(_seed)).Deterministic();
        var battle = new Battle(
            _player,
            _enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput(_playerScript).ForgetsSlot(_playerForgetSlot),
            new ScriptedInput(_enemyScript),
            rules: rules,
            emitter: emitter,
            rng: new SeededRandomSource(_seed),
            escapable: _escapable
        );
        await battle.StartFightAsync();
        return new BattleScenarioResult(emitter, _player, _enemy);
    }
}

/// <summary>Outcome of a <see cref="BattleScenario"/>: the recorded events plus both creatures.</summary>
public sealed record BattleScenarioResult(RecordingEmitter Emitter, Creature Player, Creature Enemy)
{
    public IReadOnlyList<BattleEvent> Events => Emitter.Events;

    public bool Has<T>()
        where T : BattleEvent => Emitter.Of<T>().Any();

    public IReadOnlyList<T> All<T>()
        where T : BattleEvent => Emitter.Of<T>().ToList();

    public T? First<T>()
        where T : BattleEvent => Emitter.Of<T>().FirstOrDefault();

    public int Count<T>()
        where T : BattleEvent => Emitter.Of<T>().Count();

    /// <summary>Every damage dealt to the named creature, in order.</summary>
    public IReadOnlyList<DamageDealt> DamageTo(string creatureName) =>
        Emitter.Of<DamageDealt>().Where(d => d.TargetName == creatureName).ToList();

    public string Winner => Emitter.Of<BattleEnded>().LastOrDefault()?.WinnerName ?? "";
}
