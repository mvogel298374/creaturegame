using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class AttributesAndRulesTests
{ // --- Attributes Tests ---
    [Fact]
    public void Attributes_ReceiveDamage_ReducesHP()
    {
        var attrs = new Attributes { HP = 100, MaxHP = 100 };
        attrs.ReceiveDamage(30);
        Assert.Equal(70, attrs.HP);
    }

    [Fact]
    public void Attributes_ReceiveDamage_FloorsAtZero()
    {
        var attrs = new Attributes { HP = 10, MaxHP = 100 };
        attrs.ReceiveDamage(999);
        Assert.Equal(0, attrs.HP);
    }

    [Fact]
    public void Attributes_ReceiveHealing_IncreasesHP()
    {
        var attrs = new Attributes { HP = 50, MaxHP = 100 };
        attrs.ReceiveHealing(20);
        Assert.Equal(70, attrs.HP);
    }

    [Fact]
    public void Attributes_ReceiveHealing_CapsAtMaxHP()
    {
        var attrs = new Attributes { HP = 90, MaxHP = 100 };
        attrs.ReceiveHealing(50);
        Assert.Equal(100, attrs.HP);
    }

    // --- Gen1BattleRules Contract Tests ---

    [Fact]
    public void Gen1BattleRules_RollSleepTurns_IsInRange1To7()
    {
        var rules = Gen1BattleRules.Instance;
        for (int i = 0; i < 200; i++)
            Assert.InRange(rules.RollSleepTurns(), 1, 7);
    }

    [Fact]
    public void Gen1BattleRules_RollDamageVariance_IsInRange()
    {
        var rules = Gen1BattleRules.Instance;
        double min = 217.0 / 255.0;
        for (int i = 0; i < 200; i++)
            Assert.InRange(rules.RollDamageVariance(), min, 1.0);
    }

    [Fact]
    public void Gen1BattleRules_StruggleRecoil_IsHalfDamage()
    {
        var rules = Gen1BattleRules.Instance;
        var creature = new Creature("Test") { Level = 1 };
        Assert.Equal(25, rules.CalculateStruggleRecoil(creature, 50));
    }

    [Fact]
    public void Gen1BattleRules_StruggleRecoil_MinimumOne()
    {
        var rules = Gen1BattleRules.Instance;
        var creature = new Creature("Test") { Level = 1 };
        Assert.Equal(1, rules.CalculateStruggleRecoil(creature, 1));
    }

    [Fact]
    public void Gen1BattleRules_BurnAndPoisonDenominators_Are16()
    {
        var rules = Gen1BattleRules.Instance;
        Assert.Equal(16, rules.BurnDamageDenominator);
        Assert.Equal(16, rules.PoisonDamageDenominator);
    }

    [Fact]
    public void ResetBattleState_ReplacesWholeBattleState_ClearingEveryTransientField()
    {
        // Locks in the BattleState contract: reset swaps in a fresh instance, so every
        // per-battle field returns to default. A new transient field added to BattleState
        // is covered automatically — it cannot be "forgotten" by a manual reset list.
        var creature = new Creature("Snorlax") { Level = 50 };
        creature.CalculateStats();

        var before = creature.Battle;
        creature.Battle.Status = StatusCondition.Sleep;
        creature.Battle.SleepTurns = 5;
        creature.Battle.ConfusedTurns = 3;
        creature.Battle.ToxicCounter = 7;
        creature.Battle.Stages.RaiseAttack(4);
        creature.Battle.IsRecharging = true;
        creature.Battle.IsFlinched = true;
        creature.Battle.HasLeechSeed = true;
        creature.Battle.BindingTurnsRemaining = 3;
        creature.Battle.BindingMove = new PokemonAttack(new Attack { Name = "wrap" });
        creature.Battle.BindingTarget = new Creature("Victim");
        creature.Battle.IsTwoTurnCharging = true;
        creature.Battle.ChargingMove = new PokemonAttack(
            new Attack { Name = "Dig", BaseDamage = 80 }
        );

        creature.ResetBattleState();

        Assert.NotSame(before, creature.Battle);
        Assert.Equal(StatusCondition.None, creature.Battle.Status);
        Assert.Equal(0, creature.Battle.SleepTurns);
        Assert.Equal(0, creature.Battle.ConfusedTurns);
        Assert.Equal(1, creature.Battle.ToxicCounter);
        Assert.Equal(0, creature.Battle.Stages.Attack);
        Assert.False(creature.Battle.IsRecharging);
        Assert.False(creature.Battle.IsFlinched);
        Assert.False(creature.Battle.HasLeechSeed);
        Assert.Equal(0, creature.Battle.BindingTurnsRemaining);
        Assert.Null(creature.Battle.BindingMove);
        Assert.Null(creature.Battle.BindingTarget);
        Assert.False(creature.Battle.IsTwoTurnCharging);
        Assert.Null(creature.Battle.ChargingMove);
    }
}
