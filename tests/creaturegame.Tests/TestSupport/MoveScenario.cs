using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;

namespace creaturegame.Tests.TestSupport;

/// <summary>
/// Builds two creatures with controlled stats/types, then computes <see cref="Attributes"/> and
/// overwrites them so a test isn't at the mercy of the DV/level formula.
/// </summary>
public static class TestCreatures
{
    public static Creature Make(
        string name = "Mon", int level = 50,
        DamageType? type1 = DamageType.Normal, DamageType? type2 = null,
        int hp = 200, int attack = 100, int defense = 100, int special = 100, int speed = 100,
        int baseSpeed = 100)
    {
        var c = new Creature(name) { Level = level, Type1 = type1, Type2 = type2, BaseSpeed = baseSpeed };
        c.CalculateStats();
        c.Attributes.MaxHP   = hp;
        c.Attributes.HP      = hp;
        c.Attributes.Attack  = attack;
        c.Attributes.Defense = defense;
        c.Attributes.Special = special;
        c.Attributes.Speed   = speed;
        return c;
    }
}

/// <summary>
/// "Give a move to a Pokémon and use it once." A fluent harness around a single
/// <see cref="AttackAction"/>: pick attacker/defender, rules, and RNG, then <see cref="Use"/> a
/// real move and inspect the resulting events + creature state.
/// </summary>
public sealed class MoveScenario
{
    private Creature   _attacker  = TestCreatures.Make("Attacker");
    private Creature   _defender  = TestCreatures.Make("Defender");
    private IBattleRules _rules   = AlwaysHitRules.Instance;     // default: don't fight the 1/256 miss
    private IRandomSource _rng    = new SeededRandomSource(0);
    private ITypeChart _typeChart = Gen1TypeChart.Instance;
    private IReadOnlyList<Attack> _movePool = Array.Empty<Attack>();

    public MoveScenario Attacker(Creature c) { _attacker = c; return this; }
    public MoveScenario Defender(Creature c) { _defender = c; return this; }
    public MoveScenario Rules(IBattleRules r) { _rules = r; return this; }
    public MoveScenario Rng(IRandomSource r) { _rng = r; return this; }
    public MoveScenario TypeChart(ITypeChart t) { _typeChart = t; return this; }
    /// <summary>Move pool for effects that draw from one (Metronome).</summary>
    public MoveScenario MovePool(params Attack[] pool) { _movePool = pool; return this; }

    public async Task<MoveResult> Use(Attack move)
    {
        _attacker.AddAttack(move);
        var pokeMove = _attacker.MoveSet[^1];
        var emitter  = new RecordingEmitter();
        var action   = new AttackAction(_attacker, _defender, pokeMove, _typeChart, _rules, emitter, _movePool, _rng);
        await action.ExecuteAsync();
        return new MoveResult(emitter, _attacker, _defender, pokeMove);
    }

    /// <summary>
    /// Runs the same move over <paramref name="turns"/> consecutive <see cref="AttackAction"/>s,
    /// reusing one <see cref="PokemonAttack"/> wrapper so PP and multi-turn state (two-turn charge,
    /// recharge) carry across turns exactly as they would in a real battle. Returns one
    /// <see cref="MoveResult"/> per turn (each with its own freshly-recorded events).
    /// </summary>
    public async Task<IReadOnlyList<MoveResult>> UseRepeated(Attack move, int turns)
    {
        _attacker.AddAttack(move);
        var pokeMove = _attacker.MoveSet[^1];
        var results  = new List<MoveResult>(turns);
        for (int i = 0; i < turns; i++)
        {
            var emitter = new RecordingEmitter();
            var action  = new AttackAction(_attacker, _defender, pokeMove, _typeChart, _rules, emitter, rng: _rng);
            await action.ExecuteAsync();
            results.Add(new MoveResult(emitter, _attacker, _defender, pokeMove));
        }
        return results;
    }
}

/// <summary>Outcome of one <see cref="MoveScenario.Use"/>: the recorded events plus both creatures.</summary>
public sealed record MoveResult(RecordingEmitter Emitter, Creature Attacker, Creature Defender, PokemonAttack Move)
{
    public IReadOnlyList<BattleEvent> Events => Emitter.Events;
    public bool Has<T>() where T : BattleEvent => Emitter.Of<T>().Any();
    public T? First<T>() where T : BattleEvent => Emitter.Of<T>().FirstOrDefault();
    public IReadOnlyList<DamageDealt> Hits => Emitter.Of<DamageDealt>().ToList();
    public int TotalDamage => Hits.Sum(h => h.Damage);
}
