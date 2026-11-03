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

    [Theory]
    [InlineData("water-gun", 12)] // TM12, Mt. Moon
    [InlineData("body-slam", 22)] // TM08, S.S. Anne
    [InlineData("thunderbolt", 24)] // TM24, Vermilion Gym
    [InlineData("hyper-beam", 30)] // TM15, Celadon Game Corner
    [InlineData("fissure", 47)] // TM27, Viridian Gym
    [InlineData("dream-eater", 47)] // TM42, Viridian City (after Giovanni)
    [InlineData("flash", 12)] // HM05, Route 2 aide
    public void CuratedFloors_PinKnownRedBlueSources(string name, int floor)
    {
        // Anchors the table's *values* (the live-DB pin only proves a floor exists), so a mis-keyed or
        // re-tiered entry trips a test. Sources: pokemondb.net Red/Blue TM + HM tables.
        Assert.Equal(floor, MoveMinLevels.For(name));
    }

    [Fact]
    public void TmHmMove_GetsItsCuratedMinLevelFloor_AndOtherMovesGetNone()
    {
        Assert.Equal(42, MoveImport.MapToAttack(Move(name: "earthquake")).MinLevel); // TM26, Silph Co.
        Assert.Equal(22, MoveImport.MapToAttack(Move(name: "cut")).MinLevel); // HM01, S.S. Anne
        Assert.Null(MoveImport.MapToAttack(Move(name: "hydro-pump")).MinLevel); // level-up only
    }

    private static PokeApiMove MoveWithModernText(string name)
    {
        var move = Move(name: name);
        move.EffectEntries =
        [
            new EffectEntry { Language = Named("de"), ShortEffect = "Deutscher Text." },
            new EffectEntry { Language = Named("en"), ShortEffect = "Modern text." },
        ];
        return move;
    }

    [Theory]
    [InlineData("acid", "Has a chance to lower the target’s Defense by one stage.")]
    [InlineData("psychic", "Has a chance to lower the target’s Special by one stage.")]
    [InlineData("amnesia", "Raises the user’s Special by two stages.")]
    [InlineData("minimize", "Raises the user’s evasion by one stage.")]
    [InlineData("double-edge", "User receives 1/4 the damage it inflicts in recoil.")]
    [InlineData("struggle", "User receives 1/2 the damage it inflicts in recoil.")]
    [InlineData("jump-kick", "If the user misses, it takes 1 point of damage.")]
    [InlineData("thrash", "Hits every turn for 3-4 turns, then confuses the user.")]
    [InlineData("bide", "User waits for 2-3 turns, then hits back for twice the damage it took.")]
    [InlineData(
        "rest",
        "User sleeps for two turns, fully healing itself and curing its status. Fails at full HP."
    )]
    [InlineData("metronome", "Randomly selects and uses any move except itself and Struggle.")]
    [InlineData("teleport", "Has no effect.")]
    public void GenOneDescription_ReplacesPokeApisModernText(string name, string expected)
    {
        Assert.Equal(expected, MoveImport.MapToAttack(MoveWithModernText(name)).Description);
    }

    [Fact]
    public void Gen1Descriptions_CoverExactlyTheAuditedMoves()
    {
        string[] audited =
        [
            "acid",
            "amnesia",
            "bide",
            "bind",
            "clamp",
            "conversion",
            "counter",
            "dig",
            "disable",
            "dizzy-punch",
            "double-edge",
            "earthquake",
            "explosion",
            "fire-spin",
            "fissure",
            "fly",
            "focus-energy",
            "growth",
            "guillotine",
            "gust",
            "haze",
            "high-jump-kick",
            "horn-drill",
            "hyper-beam",
            "jump-kick",
            "light-screen",
            "low-kick",
            "metronome",
            "mimic",
            "minimize",
            "mist",
            "pay-day",
            "petal-dance",
            "psychic",
            "psywave",
            "quick-attack",
            "rage",
            "recover",
            "reflect",
            "rest",
            "roar",
            "rock-slide",
            "self-destruct",
            "skull-bash",
            "sky-attack",
            "soft-boiled",
            "string-shot",
            "struggle",
            "substitute",
            "surf",
            "teleport",
            "thrash",
            "tri-attack",
            "waterfall",
            "whirlwind",
            "wrap",
        ];

        Assert.Equal(audited, MoveImport.Gen1Descriptions.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void MoveWithoutAnOverride_KeepsPokeApisEnglishText()
    {
        Assert.Equal(
            "Modern text.",
            MoveImport.MapToAttack(MoveWithModernText("pound")).Description
        );
    }

    [Theory]
    [InlineData("karate-chop", true)]
    [InlineData("razor-leaf", true)]
    [InlineData("crabhammer", true)]
    [InlineData("slash", true)]
    [InlineData("razor-wind", false)]
    [InlineData("sky-attack", false)]
    [InlineData("pound", false)]
    public void IsHighCrit_ComesFromPokeredsList(string name, bool isHighCrit)
    {
        Assert.Equal(isHighCrit, MoveImport.MapToAttack(Move(name: name)).IsHighCrit);
    }

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
    public void StatusAilment_PastValuesChanceWinsOverModernAilmentChance()
    {
        var move = Move(name: "test-burn-move", type: "fire");
        move.EffectChance = 10;
        move.PastValues = [new() { EffectChance = 30 }];
        move.Meta = new MoveMeta { Ailment = Named("burn"), AilmentChance = 10 };

        var attack = MoveImport.MapToAttack(move);

        Assert.Equal(30, attack.EffectChance);
    }

    [Fact]
    public void Minimize_RaisesEvasionByOneStage_NotPokeApisTwo()
    {
        var move = Move(name: "minimize", power: null, damageClass: "status");
        move.Target = Named("user");
        move.StatChanges = [new() { Change = 2, Stat = Named("evasion") }];

        var attack = MoveImport.MapToAttack(move);

        Assert.Equal(StageStat.Evasion, attack.StatEffectStat);
        Assert.Equal(1, attack.StatEffectDelta);
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
    public void Layer2Correction_Psychic_SpecialDropChanceIs33()
    {
        var move = Move(name: "psychic", power: 90, type: "psychic", damageClass: "special");
        move.EffectChance = 10;
        move.StatChanges = [new() { Change = -1, Stat = Named("special-defense") }];

        var attack = MoveImport.MapToAttack(move);

        Assert.Equal(StageStat.Special, attack.StatEffectStat);
        Assert.Equal(-1, attack.StatEffectDelta);
        Assert.Equal(StageTarget.Foe, attack.StatEffectTarget);
        Assert.Equal(33, attack.StatEffectChance);
        Assert.Equal(33, attack.EffectChance);
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
