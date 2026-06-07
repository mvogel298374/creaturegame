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
}

/// <summary>
/// A fully configurable <see cref="IBattleRules"/> double for interaction tests: every roll defaults
/// to real Gen 1 behaviour, and a fluent setter pins just the ones a test needs to control. Combine
/// with a seeded RNG on the <see cref="BattleScenario"/> for full determinism. The common baseline —
/// always hit, no crit, no damage variance — is one call (<see cref="Deterministic"/>).
/// </summary>
public sealed class ScriptableRules : DelegatingBattleRules
{
    private bool _alwaysHit;
    private bool _noCrit;
    private bool _noVariance;
    private bool _forceSecondary;
    private int? _sleepTurns;
    private int? _confusionTurns;
    private int? _rampageTurns;
    private int? _bideTurns;
    private int? _bindingTurns;
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

    public ScriptableRules MultiHits(int n)
    {
        _multiHitCount = n;
        return this;
    }

    public override int GetHitThreshold(int acc, int accStage, int evaStage) =>
        _alwaysHit ? 256 : base.GetHitThreshold(acc, accStage, evaStage);

    public override double GetCritChance(Creature a, Attack m) =>
        _noCrit ? 0.0 : base.GetCritChance(a, m);

    public override double RollDamageVariance() => _noVariance ? 1.0 : base.RollDamageVariance();

    public override int GetSecondaryEffectChance(Attack m, SecondaryEffectKind e) =>
        _forceSecondary ? 100 : base.GetSecondaryEffectChance(m, e);

    public override int RollSleepTurns() => _sleepTurns ?? base.RollSleepTurns();

    public override int RollConfusionTurns() => _confusionTurns ?? base.RollConfusionTurns();

    public override int RollRampageTurns() => _rampageTurns ?? base.RollRampageTurns();

    public override int RollBideTurns() => _bideTurns ?? base.RollBideTurns();

    public override int RollBindingTurns() => _bindingTurns ?? base.RollBindingTurns();

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
    private IBattleRules _rules = new ScriptableRules().Deterministic();
    private int _seed;

    public BattleScenario Player(Creature c)
    {
        _player = c;
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

    public async Task<BattleScenarioResult> RunAsync()
    {
        var emitter = new RecordingEmitter();
        var battle = new Battle(
            _player,
            _enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput(_playerScript),
            new ScriptedInput(_enemyScript),
            rules: _rules,
            emitter: emitter,
            rng: new SeededRandomSource(_seed)
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
