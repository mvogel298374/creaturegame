using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Regression tests for two battle bugs:
///  1. Confusion-inflicting moves (Supersonic, Confuse Ray, Psybeam…) did nothing because no
///     move effect ever set <c>ConfusedTurns</c> — confusion is a per-battle counter, not a
///     <see cref="StatusCondition"/>, and the importer dropped the "confusion" ailment.
///  2. The enemy used <see cref="AutoSelectInput"/>, which always returns move slot 0, so it
///     spammed whatever low-level move sat there (often a status move like Leer).
/// </summary>
public class ConfusionAndInputTests
{
    private static Creature MakeCreature(string name)
    {
        var c = new Creature(name) { Level = 50 };
        c.CalculateStats();   // base stats default to 0 → ~60 HP at L50; enough to be alive
        return c;
    }

    // --- Confusion application ----------------------------------------------

    private static Attack ConfuseMove(int chance = 100, int power = 0) => new("supersonic", "")
    {
        Id = 48, BaseDamage = power, Accuracy = 100, AttackType = AttackType.Undefined,
        DamageType = DamageType.Normal, Effect = MoveEffect.Confuse,
        EffectChance = chance == 100 ? (int?)null : chance,   // pure confusion moves carry no chance
    };

    [Fact]
    public async Task ConfuseMove_AppliesConfusedTurns_AndEmitsConfusionStarted()
    {
        var attacker = MakeCreature("Attacker");
        var target   = MakeCreature("Target");
        attacker.MoveSet.Clear();
        attacker.AddAttack(ConfuseMove());

        var emitter = new RecordingEmitter();
        var action  = new AttackAction(attacker, target, attacker.MoveSet[0], new Gen1TypeChart(),
            AlwaysHit.Instance, emitter, rng: new SeededRandomSource(1));
        await action.ExecuteAsync();

        Assert.True(target.ConfusedTurns > 0, "Supersonic should have confused the target");
        Assert.Contains(emitter.Events, e => e is ConfusionStarted cs && cs.TargetName == "Target");
    }

    [Fact]
    public async Task ConfuseMove_DoesNotStack_WhenTargetAlreadyConfused()
    {
        var attacker = MakeCreature("Attacker");
        var target   = MakeCreature("Target");
        target.ConfusedTurns = 3;
        attacker.MoveSet.Clear();
        attacker.AddAttack(ConfuseMove());

        var emitter = new RecordingEmitter();
        var action  = new AttackAction(attacker, target, attacker.MoveSet[0], new Gen1TypeChart(),
            AlwaysHit.Instance, emitter, rng: new SeededRandomSource(1));
        await action.ExecuteAsync();

        Assert.Equal(3, target.ConfusedTurns);   // unchanged
        Assert.DoesNotContain(emitter.Events, e => e is ConfusionStarted);
    }

    [Fact]
    public async Task ConfuseMove_WithZeroChance_NeverConfuses()
    {
        var attacker = MakeCreature("Attacker");
        var target   = MakeCreature("Target");
        attacker.MoveSet.Clear();
        attacker.AddAttack(ConfuseMove(chance: 0, power: 65));   // damaging move, 0% secondary

        var emitter = new RecordingEmitter();
        var action  = new AttackAction(attacker, target, attacker.MoveSet[0], new Gen1TypeChart(),
            AlwaysHit.Instance, emitter, rng: new SeededRandomSource(1));
        await action.ExecuteAsync();

        Assert.Equal(0, target.ConfusedTurns);
    }

    // --- RandomMoveInput (Bug 1) --------------------------------------------

    [Fact]
    public async Task RandomMoveInput_OnlyPicksMovesWithRemainingPP()
    {
        var c = MakeCreature("Mon");
        c.MoveSet.Clear();
        c.AddAttack(new Attack { Id = 1, Name = "Leer",   BaseDamage = 0,  Accuracy = 100, PowerPointsMax = 5 });
        c.AddAttack(new Attack { Id = 2, Name = "Tackle", BaseDamage = 40, Accuracy = 100, PowerPointsMax = 5 });
        c.MoveSet[1].PowerPointsCurrent = 0;   // Tackle exhausted → only Leer is selectable

        var input = new RandomMoveInput(new SeededRandomSource(7));
        for (int i = 0; i < 20; i++)
        {
            var chosen = await input.ChooseMoveAsync(Ctx(c));
            Assert.Equal("Leer", chosen.Base.Name);
        }
    }

    [Fact]
    public async Task RandomMoveInput_OverManyTurns_UsesMoreThanJustSlotZero()
    {
        // The bug: AutoSelectInput always returns slot 0. RandomMoveInput must spread its
        // choices across the available moves, so an enemy with Leer in slot 0 still attacks.
        var c = MakeCreature("Mon");
        c.MoveSet.Clear();
        c.AddAttack(new Attack { Id = 1, Name = "Leer",       BaseDamage = 0,  Accuracy = 100, PowerPointsMax = 99 });
        c.AddAttack(new Attack { Id = 2, Name = "Ember",      BaseDamage = 40, Accuracy = 100, PowerPointsMax = 99 });
        c.AddAttack(new Attack { Id = 3, Name = "Slash",      BaseDamage = 70, Accuracy = 100, PowerPointsMax = 99 });
        c.AddAttack(new Attack { Id = 4, Name = "Fire Spin",  BaseDamage = 15, Accuracy = 100, PowerPointsMax = 99 });

        var input = new RandomMoveInput(new SeededRandomSource(3));
        var seen  = new HashSet<string>();
        for (int i = 0; i < 50; i++)
            seen.Add((await input.ChooseMoveAsync(Ctx(c))).Base.Name!);

        Assert.True(seen.Count >= 3, $"Expected variety across moves, saw only: {string.Join(", ", seen)}");
        Assert.Contains("Slash", seen);   // a damaging move is actually used
    }

    [Fact]
    public async Task RandomMoveInput_SameSeed_IsDeterministic()
    {
        var c = MakeCreature("Mon");
        c.MoveSet.Clear();
        c.AddAttack(new Attack { Id = 1, Name = "A", BaseDamage = 10, Accuracy = 100, PowerPointsMax = 99 });
        c.AddAttack(new Attack { Id = 2, Name = "B", BaseDamage = 10, Accuracy = 100, PowerPointsMax = 99 });
        c.AddAttack(new Attack { Id = 3, Name = "C", BaseDamage = 10, Accuracy = 100, PowerPointsMax = 99 });

        var a = new RandomMoveInput(new SeededRandomSource(99));
        var b = new RandomMoveInput(new SeededRandomSource(99));
        for (int i = 0; i < 30; i++)
            Assert.Equal((await a.ChooseMoveAsync(Ctx(c))).Base.Name, (await b.ChooseMoveAsync(Ctx(c))).Base.Name);
    }

    // --- Helpers -------------------------------------------------------------

    private static TurnContext Ctx(Creature attacker) => new()
    {
        Attacker = attacker, Defender = attacker, TypeChart = new Gen1TypeChart(),
        Rules = Gen1BattleRules.Instance, TurnNumber = 1,
    };

    private sealed class RecordingEmitter : IBattleEventEmitter
    {
        private readonly List<BattleEvent> _events = [];
        public IReadOnlyList<BattleEvent> Events => _events;
        public void Emit(BattleEvent evt) => _events.Add(evt);
    }

    /// <summary>Always-hit rules so the accuracy roll never interferes with the effect under test.</summary>
    private sealed class AlwaysHit : IBattleRules
    {
        public static readonly AlwaysHit Instance = new();
        public bool   CanThawFrozenTarget(Attack m)             => Gen1BattleRules.Instance.CanThawFrozenTarget(m);
        public int    FreezeRandomThawPercent                   => Gen1BattleRules.Instance.FreezeRandomThawPercent;
        public double RollDamageVariance()                      => 1.0;
        public int    RollSleepTurns()                          => Gen1BattleRules.Instance.RollSleepTurns();
        public int    RollConfusionTurns()                      => Gen1BattleRules.Instance.RollConfusionTurns();
        public int    CalculateStruggleRecoil(Creature s, int d) => Gen1BattleRules.Instance.CalculateStruggleRecoil(s, d);
        public int    BurnDamageDenominator                     => 16;
        public int    PoisonDamageDenominator                   => 16;
        public double BadPoisonDamageFraction(int c)            => Gen1BattleRules.Instance.BadPoisonDamageFraction(c);
        public double GetStatMultiplier(int stage)              => Gen1BattleRules.Instance.GetStatMultiplier(stage);
        public double GetAccuracyStageMultiplier(int stage)     => Gen1BattleRules.Instance.GetAccuracyStageMultiplier(stage);
        public int    GetHitThreshold(int acc, int a, int e)    => 256;
        public int    AccuracyRollBound                         => 256;
        public double GetCritChance(Creature a, Attack m)       => 0.0;
        public double CritMultiplier                            => 2.0;
        public bool   CritIgnoresStatStages                     => true;
        public int    RollBindingTurns()                        => Gen1BattleRules.Instance.RollBindingTurns();
        public int    BindingDamageDenominator                  => 16;
        public int    CalculateXpAwarded(int b, int l)          => Gen1BattleRules.Instance.CalculateXpAwarded(b, l);
        public int    GetOffensiveStat(Creature a, AttackType t) => Gen1BattleRules.Instance.GetOffensiveStat(a, t);
        public int    GetDefensiveStat(Creature d, AttackType t) => Gen1BattleRules.Instance.GetDefensiveStat(d, t);
    }
}
