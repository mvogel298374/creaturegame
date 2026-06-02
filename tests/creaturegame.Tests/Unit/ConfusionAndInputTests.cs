using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

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
        var c = new Creature(name)
        {
            Level = 50, BaseHP = 60, BaseAttack = 60, BaseDefense = 60, BaseSpecial = 60, BaseSpeed = 60,
        };
        c.CalculateStats();
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
            AlwaysHitRules.Instance, emitter, rng: new SeededRandomSource(1));
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
            AlwaysHitRules.Instance, emitter, rng: new SeededRandomSource(1));
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
            AlwaysHitRules.Instance, emitter, rng: new SeededRandomSource(1));
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

}
