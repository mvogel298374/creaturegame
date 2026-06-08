using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Conversion (Gen 1): the user copies the target's Type1/Type2 onto itself (no damage). This is the
/// Gen 1 mechanic — Gen 2+ changes Conversion to match one of the user's own moves instead. Like
/// Transform, the change is undone on the per-battle reset / at battle end, so it never leaks into the
/// permanent Creature. The last test proves the shared identity-snapshot machinery: a Conversion after
/// a Transform still restores the TRUE original (the snapshot is taken only once).
/// </summary>
[Collection(MovesCollection.Name)]
public class ConversionContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task ConversionCopiesTheTargetsTypesOntoTheUser()
    {
        var attacker = TestCreatures.Make("A", type1: DamageType.Normal, type2: null);
        var target = TestCreatures.Make("B", type1: DamageType.Water, type2: DamageType.Ground);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(target)
            .Use(Move("conversion"));

        Assert.False(result.Has<DamageDealt>(), "Conversion deals no damage");
        // Self-affecting: it must never apply a status to the foe (guards the pre-handler TryApplyStatus
        // path — the StatusEffect==None data pin's behavioural twin).
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
        Assert.Equal(DamageType.Water, attacker.Type1);
        Assert.Equal(DamageType.Ground, attacker.Type2);

        var converted = result.First<ConvertedType>();
        Assert.NotNull(converted);
        Assert.Equal(DamageType.Water, converted!.NewType);
    }

    [Fact]
    public async Task ConversionLeavesStatsAndMovesetUntouched()
    {
        // Conversion only changes types — not stats or moves (that's Transform's job).
        var attacker = TestCreatures.Make("A", type1: DamageType.Normal, attack: 77);
        attacker.AddAttack(Move("pound"));
        var target = TestCreatures.Make("B", type1: DamageType.Electric, attack: 200);

        await new MoveScenario().Attacker(attacker).Defender(target).Use(Move("conversion"));

        Assert.Equal(DamageType.Electric, attacker.Type1);
        Assert.Equal(77, attacker.Attributes.Attack); // stats unchanged
        // Moveset unchanged by Conversion ("conversion" itself is added by the harness when it's used).
        Assert.Equal(new[] { "pound", "conversion" }, attacker.MoveSet.Select(m => m.Base.Name));
    }

    [Fact]
    public async Task ResettingBattleStateRevertsAConversion()
    {
        var attacker = TestCreatures.Make("A", type1: DamageType.Normal);
        var target = TestCreatures.Make("B", type1: DamageType.Psychic, type2: DamageType.Flying);

        await new MoveScenario().Attacker(attacker).Defender(target).Use(Move("conversion"));
        Assert.Equal(DamageType.Psychic, attacker.Type1); // converted

        attacker.ResetBattleState();

        Assert.Equal(DamageType.Normal, attacker.Type1); // reverted
        Assert.Null(attacker.Type2);
    }

    [Fact]
    public async Task ConversionAfterTransformStillRestoresTheTrueOriginal()
    {
        // The shared snapshot is taken only on the FIRST identity mutation — so a Conversion that
        // follows a Transform must not overwrite it. After both, a reset restores the pre-Transform
        // identity (Normal type, original stats and moveset), not the intermediate transformed one.
        var attacker = TestCreatures.Make("A", type1: DamageType.Normal, attack: 40);
        var transformTarget = TestCreatures.Make("T", type1: DamageType.Ghost, attack: 210);
        transformTarget.AddAttack(Move("tackle"));
        var conversionTarget = TestCreatures.Make("C", type1: DamageType.Water);

        await new MoveScenario()
            .Attacker(attacker)
            .Defender(transformTarget)
            .Use(Move("transform"));
        Assert.Equal(DamageType.Ghost, attacker.Type1);
        Assert.Equal(210, attacker.Attributes.Attack);

        await new MoveScenario()
            .Attacker(attacker)
            .Defender(conversionTarget)
            .Use(Move("conversion"));
        Assert.Equal(DamageType.Water, attacker.Type1); // re-typed by Conversion
        Assert.Equal(210, attacker.Attributes.Attack); // still the transformed stats

        attacker.ResetBattleState();

        // Back to the true original captured before Transform, not the transformed/converted state.
        Assert.Equal(DamageType.Normal, attacker.Type1);
        Assert.Equal(40, attacker.Attributes.Attack);
    }
}
