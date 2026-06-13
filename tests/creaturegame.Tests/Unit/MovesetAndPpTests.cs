using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class MovesetAndPpTests
{ // --- PP Tracking Tests ---
    [Fact]
    public async Task PP_DecrementsOnUse()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();

        var baseAttack = new Attack
        {
            Name = "Tackle",
            BaseDamage = 40,
            Accuracy = 100,
            PowerPointsMax = 5,
        };
        attacker.AddAttack(baseAttack);
        var move = attacker.MoveSet[0];

        int ppBefore = move.PowerPointsCurrent;
        var action = new AttackAction(
            attacker,
            defender,
            move,
            new Gen1TypeChart(),
            emitter: ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        Assert.Equal(ppBefore - 1, move.PowerPointsCurrent);
    }

    [Fact]
    public async Task PP_StruggleUsedWhenPPIsZero()
    {
        var attacker = new Creature("Attacker") { Level = 10 };
        attacker.CalculateStats();
        attacker.Attributes.Attack = 50;
        var defender = new Creature("Defender") { Level = 10 };
        defender.CalculateStats();
        defender.Attributes.Defense = 50;
        int defenderHpBefore = defender.Attributes.HP;

        var baseAttack = new Attack
        {
            Name = "Tackle",
            BaseDamage = 40,
            Accuracy = 100,
            PowerPointsMax = 1,
        };
        attacker.AddAttack(baseAttack);
        var move = attacker.MoveSet[0];
        move.PowerPointsCurrent = 0; // force PP exhausted

        // null signals AttackAction to use Struggle — mirrors what Battle does when out of PP
        var action = new AttackAction(
            attacker,
            defender,
            null,
            new Gen1TypeChart(),
            emitter: ConsoleBattleEventEmitter.Instance
        );
        await action.ExecuteAsync();

        // Defender should have taken damage (Struggle fired)
        Assert.True(defender.Attributes.HP < defenderHpBefore);
        // Attacker should have taken recoil
        Assert.True(attacker.Attributes.HP < attacker.Attributes.MaxHP);
        // PP should remain 0 (not decremented further)
        Assert.Equal(0, move.PowerPointsCurrent);
    }

    [Fact]
    public void FullHeal_RestoresHpAndAllPp_AndClearsMajorStatus()
    {
        var creature = new Creature("Healme") { Level = 20 };
        creature.CalculateStats();
        creature.AddAttack(
            new Attack
            {
                Id = 1,
                Name = "Tackle",
                PowerPointsMax = 35,
            }
        );
        creature.AddAttack(
            new Attack
            {
                Id = 2,
                Name = "Ember",
                PowerPointsMax = 25,
            }
        );

        // Wound it: HP down, PP spent on both moves, and a persisting major status set.
        creature.Attributes.HP = 1;
        creature.MoveSet[0].PowerPointsCurrent = 0;
        creature.MoveSet[1].PowerPointsCurrent = 3;
        creature.Battle.Status = StatusCondition.BadPoison;
        creature.Battle.ToxicCounter = 5;
        creature.Battle.SleepTurns = 2;

        creature.FullHeal();

        Assert.Equal(creature.Attributes.MaxHP, creature.Attributes.HP);
        Assert.Equal(35, creature.MoveSet[0].PowerPointsCurrent);
        Assert.Equal(25, creature.MoveSet[1].PowerPointsCurrent);
        Assert.Equal(StatusCondition.None, creature.Battle.Status);
        Assert.Equal(1, creature.Battle.ToxicCounter); // back to baseline
        Assert.Equal(0, creature.Battle.SleepTurns);
    }

    // --- AddAttack Constraint Tests ---

    [Fact]
    public void AddAttack_FifthMoveRejected()
    {
        var creature = new Creature("Test") { Level = 1 };
        for (int i = 1; i <= 4; i++)
            creature.AddAttack(new Attack { Id = i, Name = $"Move{i}" });

        bool result = creature.AddAttack(new Attack { Id = 5, Name = "Fifth" });

        Assert.False(result);
        Assert.Equal(4, creature.MoveSet.Count);
    }

    [Fact]
    public void AddAttack_DuplicateIdRejected()
    {
        var creature = new Creature("Test") { Level = 1 };
        creature.AddAttack(new Attack { Id = 1, Name = "Tackle" });

        bool result = creature.AddAttack(new Attack { Id = 1, Name = "AnotherTackle" });

        Assert.False(result);
        Assert.Single(creature.MoveSet);
    }
}
