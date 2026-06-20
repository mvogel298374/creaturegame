using creaturegame.Attacks;
using creaturegame.Creatures;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Unit tests for the importer's PokeAPI→Attack mapping (<see cref="MoveImport.MapToAttack"/>). Pure
/// DTO→model mapping — no network, no database — so it exercises the actual importer code (the live-db
/// contract tests only read already-imported rows and can't catch a mapping regression without a
/// re-import). One test per concern of the staged mapper: past_values resolution, the type-derived
/// physical/special split, the by-id damage categories, status/stat-stage effects, the name→effect map,
/// and the layer-2 Gen 1 corrections.
/// </summary>
public class MoveMappingTests
{
    private static NamedApiResource Named(string name) => new() { Name = name };

    // A minimal damaging-move DTO; tests override only the fields they care about.
    private static PokeApiMove Move(
        int id = 1,
        string name = "test-move",
        int? power = 50,
        int? accuracy = 100,
        int? pp = 20,
        string type = "normal",
        string damageClass = "physical"
    ) =>
        new()
        {
            Id = id,
            Name = name,
            Power = power,
            Accuracy = accuracy,
            Pp = pp,
            Type = Named(type),
            DamageClass = Named(damageClass),
        };

    [Fact]
    public void PastValues_EarliestEntry_SuppliesGen1PowerAndAccuracy()
    {
        // Modern stats differ from Gen 1; the earliest past_values entry is the Gen 1 value.
        var move = Move(power: 120, accuracy: 100);
        move.PastValues = [new() { Power = 65, Accuracy = 90 }];

        var attack = MoveImport.MapToAttack(move);

        Assert.Equal(65, attack.BaseDamage);
        Assert.Equal(90, attack.Accuracy);
    }

    [Fact]
    public void NoPastValues_FallsBackToModernStats()
    {
        var attack = MoveImport.MapToAttack(Move(power: 40, accuracy: 95, pp: 35));

        Assert.Equal(40, attack.BaseDamage);
        Assert.Equal(95, attack.Accuracy);
        Assert.Equal(35, attack.PowerPointsMax);
    }

    [Fact]
    public void PhysicalSplit_DerivedFromType_NotModernDamageClass()
    {
        // Gen 1: a damaging move's physical/special split is decided by TYPE, not the modern
        // per-move damage_class. A Fire move that PokeAPI calls "physical" is Special in Gen 1.
        var attack = MoveImport.MapToAttack(Move(type: "fire", damageClass: "physical"));

        Assert.Equal(DamageType.Fire, attack.DamageType);
        Assert.Equal(AttackType.Special, attack.AttackType);
    }

    [Fact]
    public void StatusMove_DamageClassStatus_LeavesAttackTypeUndefined()
    {
        var attack = MoveImport.MapToAttack(
            Move(name: "growl", power: null, type: "normal", damageClass: "status")
        );

        Assert.Equal(AttackType.Undefined, attack.AttackType);
    }

    [Fact]
    public void SelfDestruct_ById_MapsToSelfDestructCategory()
    {
        var attack = MoveImport.MapToAttack(Move(id: 120, name: "self-destruct"));

        Assert.Equal(DamageCategory.SelfDestruct, attack.DamageCategory);
    }

    [Fact]
    public void SeismicToss_ById_IsLevelBased()
    {
        var attack = MoveImport.MapToAttack(Move(id: 69, name: "seismic-toss"));

        Assert.Equal(DamageCategory.LevelBased, attack.DamageCategory);
    }

    [Fact]
    public void SonicBoom_ById_IsFixed20Damage()
    {
        var attack = MoveImport.MapToAttack(Move(id: 49, name: "sonic-boom"));

        Assert.Equal(DamageCategory.Fixed, attack.DamageCategory);
        Assert.Equal(20, attack.FixedDamageValue);
    }

    [Fact]
    public void DragonRage_ById_IsFixed40Damage()
    {
        var attack = MoveImport.MapToAttack(Move(id: 82, name: "dragon-rage"));

        Assert.Equal(DamageCategory.Fixed, attack.DamageCategory);
        Assert.Equal(40, attack.FixedDamageValue);
    }

    [Fact]
    public void Swift_ById_NeverMisses()
    {
        var attack = MoveImport.MapToAttack(Move(id: 129, name: "swift"));

        Assert.True(attack.NeverMisses);
    }

    [Fact]
    public void StatusAilment_SetsStatusAndAilmentChance()
    {
        var move = Move(name: "thunderbolt", type: "electric");
        move.Meta = new MoveMeta { Ailment = Named("paralysis"), AilmentChance = 10 };

        var attack = MoveImport.MapToAttack(move);

        Assert.Equal(StatusCondition.Paralysis, attack.StatusEffect);
        Assert.Equal(10, attack.EffectChance);
    }

    [Fact]
    public void StatChange_SelfTarget_MapsStatStageEffectAtFullChance()
    {
        // A pure stat move (no damage) always succeeds → chance 100.
        var move = Move(name: "swords-dance", power: null, damageClass: "status");
        move.Target = Named("user");
        move.StatChanges = [new() { Change = 2, Stat = Named("attack") }];

        var attack = MoveImport.MapToAttack(move);

        Assert.Equal(StageStat.Attack, attack.StatEffectStat);
        Assert.Equal(2, attack.StatEffectDelta);
        Assert.Equal(StageTarget.Self, attack.StatEffectTarget);
        Assert.Equal(100, attack.StatEffectChance);
    }

    [Fact]
    public void NamedEffect_Wrap_MapsToBinding()
    {
        var attack = MoveImport.MapToAttack(Move(name: "wrap"));

        Assert.Equal(MoveEffect.Binding, attack.Effect);
    }

    [Fact]
    public void FixedCountMultiHit_DoubleKick_StrikesTwice()
    {
        var attack = MoveImport.MapToAttack(Move(name: "double-kick", type: "fighting"));

        Assert.Equal(MoveEffect.MultiHit, attack.Effect);
        Assert.Equal(2, attack.MultiHitCount);
    }

    [Fact]
    public void Layer2Correction_FireBlast_BurnChanceIs30()
    {
        // Gen 1 Fire Blast is 30% burn (modern: 10%); the correction runs last and wins.
        var move = Move(name: "fire-blast", type: "fire");
        move.Meta = new MoveMeta { Ailment = Named("burn"), AilmentChance = 10 };

        var attack = MoveImport.MapToAttack(move);

        Assert.Equal(StatusCondition.Burn, attack.StatusEffect);
        Assert.Equal(30, attack.EffectChance);
    }

    [Fact]
    public void Layer2Correction_Toxic_PromotedToBadPoison()
    {
        // PokeAPI reports Toxic's ailment as plain "poison"; the correction promotes it to BadPoison.
        var move = Move(name: "toxic", power: null, damageClass: "status");
        move.Meta = new MoveMeta { Ailment = Named("poison"), AilmentChance = 100 };

        var attack = MoveImport.MapToAttack(move);

        Assert.Equal(StatusCondition.BadPoison, attack.StatusEffect);
    }
}
