using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The four AI scoring building blocks (<see cref="IMoveEvaluator"/>s). Each measures one battle dimension;
/// these pin the sign and rough magnitude of each so a brain composed from them behaves predictably.
/// </summary>
public class MoveEvaluatorTests
{
    private static TurnContext Context(Creature attacker, Creature defender) =>
        new()
        {
            Attacker = attacker,
            Defender = defender,
            TypeChart = Gen1TypeChart.Instance,
            Rules = Gen1BattleRules.Instance,
            TurnNumber = 1,
        };

    private static PokemonAttack Move(
        string name,
        int baseDamage = 0,
        DamageType type = DamageType.Normal,
        int accuracy = 100
    ) =>
        new(
            new Attack(name, name)
            {
                BaseDamage = baseDamage,
                DamageType = type,
                Accuracy = accuracy,
            }
        );

    // ── DamageEvaluator ──────────────────────────────────────────────────────

    [Fact]
    public void Damage_StrongerMoveScoresHigherThanWeaker()
    {
        var attacker = TestCreatures.Make("A");
        var defender = TestCreatures.Make("D", hp: 300);
        var eval = new DamageEvaluator();

        double strong = eval.Score(Move("Strong", baseDamage: 120), Context(attacker, defender));
        double weak = eval.Score(Move("Weak", baseDamage: 30), Context(attacker, defender));

        Assert.True(strong > weak);
        Assert.True(weak > 0);
    }

    [Fact]
    public void Damage_NonDamagingMoveScoresZero()
    {
        var ctx = Context(TestCreatures.Make("A"), TestCreatures.Make("D"));
        Assert.Equal(0, new DamageEvaluator().Score(Move("Growl"), ctx));
    }

    [Fact]
    public void Damage_PotentialKnockOutScoresAboveOne()
    {
        var attacker = TestCreatures.Make("A");
        var defender = TestCreatures.Make("D", hp: 8); // frail enough that a strong hit is lethal
        double score = new DamageEvaluator().Score(
            Move("Finisher", baseDamage: 150),
            Context(attacker, defender)
        );
        Assert.True(score > 1.0);
    }

    [Fact]
    public void Damage_AccuracyDiscountsTheScoreProportionally()
    {
        var attacker = TestCreatures.Make("A");
        var defender = TestCreatures.Make("D", hp: 300);
        var eval = new DamageEvaluator();

        double sure = eval.Score(
            Move("Sure", baseDamage: 60, accuracy: 100),
            Context(attacker, defender)
        );
        double shaky = eval.Score(
            Move("Shaky", baseDamage: 60, accuracy: 50),
            Context(attacker, defender)
        );

        Assert.Equal(sure * 0.5, shaky, precision: 6);
    }

    // ── TypeEffectivenessEvaluator ───────────────────────────────────────────

    [Fact]
    public void Type_SuperEffectiveIsEncouraged_NotVeryEffectiveDiscouraged()
    {
        var attacker = TestCreatures.Make("A");
        var fireFoe = TestCreatures.Make("Fire", type1: DamageType.Fire);
        var waterFoe = TestCreatures.Make("Water", type1: DamageType.Water);
        var eval = new TypeEffectivenessEvaluator();

        // Water → Fire = 2× (super effective); Fire → Water = 0.5× (resisted).
        double superEff = eval.Score(
            Move("Surf", baseDamage: 90, type: DamageType.Water),
            Context(attacker, fireFoe)
        );
        double resisted = eval.Score(
            Move("Ember", baseDamage: 40, type: DamageType.Fire),
            Context(attacker, waterFoe)
        );

        Assert.True(superEff > 0);
        Assert.True(resisted < 0);
    }

    [Fact]
    public void Type_NoEffectIsStronglyPenalised()
    {
        var attacker = TestCreatures.Make("A");
        var ghost = TestCreatures.Make("Ghost", type1: DamageType.Ghost);
        // Gen 1: Normal → Ghost = 0× (immune).
        double score = new TypeEffectivenessEvaluator().Score(
            Move("Tackle", baseDamage: 35, type: DamageType.Normal),
            Context(attacker, ghost)
        );
        Assert.True(score <= -2.0);
    }

    [Fact]
    public void Type_NonDamagingMoveScoresZero()
    {
        var ctx = Context(TestCreatures.Make("A"), TestCreatures.Make("D"));
        Assert.Equal(0, new TypeEffectivenessEvaluator().Score(Move("Growl"), ctx));
    }

    // ── StatStageMoveEvaluator ───────────────────────────────────────────────

    private static PokemonAttack StatMove(StageStat stat, int delta, StageTarget target) =>
        new(
            new Attack("StatMove", "StatMove")
            {
                StatEffectStat = stat,
                StatEffectDelta = delta,
                StatEffectTarget = target,
            }
        );

    [Fact]
    public void StatStage_SelfBuffWithHeadroomIsPositive()
    {
        var ctx = Context(TestCreatures.Make("A"), TestCreatures.Make("D"));
        double score = new StatStageMoveEvaluator().Score(
            StatMove(StageStat.Attack, +1, StageTarget.Self),
            ctx
        );
        Assert.True(score > 0);
    }

    [Fact]
    public void StatStage_RaisingAMaxedStatIsPenalised()
    {
        var attacker = TestCreatures.Make("A");
        attacker.Battle.Stages.RaiseAttack(6); // already at +6
        double score = new StatStageMoveEvaluator().Score(
            StatMove(StageStat.Attack, +1, StageTarget.Self),
            Context(attacker, TestCreatures.Make("D"))
        );
        Assert.True(score < 0);
    }

    [Fact]
    public void StatStage_LoweringAnUntouchedFoeStatIsPositive()
    {
        var ctx = Context(TestCreatures.Make("A"), TestCreatures.Make("D"));
        double score = new StatStageMoveEvaluator().Score(
            StatMove(StageStat.Defense, -1, StageTarget.Foe),
            ctx
        );
        Assert.True(score > 0);
    }

    // ── StatusMoveEvaluator ──────────────────────────────────────────────────

    private static PokemonAttack StatusMove(StatusCondition status) =>
        new(new Attack("StatusMove", "StatusMove") { StatusEffect = status });

    [Fact]
    public void Status_FreshStatusOnHealthyFoeIsValued()
    {
        var ctx = Context(TestCreatures.Make("A"), TestCreatures.Make("D"));
        double score = new StatusMoveEvaluator().Score(StatusMove(StatusCondition.Sleep), ctx);
        Assert.True(score > 0);
    }

    [Fact]
    public void Status_RedundantWhenFoeAlreadyStatused()
    {
        var foe = TestCreatures.Make("D");
        foe.Battle.Status = StatusCondition.Poison;
        double score = new StatusMoveEvaluator().Score(
            StatusMove(StatusCondition.Sleep),
            Context(TestCreatures.Make("A"), foe)
        );
        Assert.True(score < 0);
    }

    [Fact]
    public void Status_WastedAgainstATypeImmuneFoe()
    {
        var fireFoe = TestCreatures.Make("Fire", type1: DamageType.Fire);
        double score = new StatusMoveEvaluator().Score(
            StatusMove(StatusCondition.Burn),
            Context(TestCreatures.Make("A"), fireFoe)
        );
        Assert.True(score < 0);
    }

    [Fact]
    public void Status_FreezeIsValuedAgainstAnIceFoe_NoGen1FreezeImmunity()
    {
        // Gen 1 has NO type-based Freeze immunity — Ice-types CAN be frozen — so the AI must still value a
        // Freeze move here (immunity is the authoritative IBattleRules.CanReceiveStatus rule, not an inline
        // guess). This pins the §5.0.1 leak the seam review caught.
        var iceFoe = TestCreatures.Make("Ice", type1: DamageType.Ice);
        double score = new StatusMoveEvaluator().Score(
            StatusMove(StatusCondition.Freeze),
            Context(TestCreatures.Make("A"), iceFoe)
        );
        Assert.True(score > 0);
    }

    [Fact]
    public void Status_NonStatusMoveScoresZero()
    {
        var ctx = Context(TestCreatures.Make("A"), TestCreatures.Make("D"));
        Assert.Equal(0, new StatusMoveEvaluator().Score(Move("Tackle", baseDamage: 35), ctx));
    }
}
