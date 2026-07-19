using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Gen 1 type immunity on <b>damaging</b> moves: a 0× target takes nothing at all — no damage, no
/// secondary status / stat drop / flinch, no recoil back on the user. The assertions target the
/// quirk itself (per <c>GENERATION_SEAMS.md §5.0.1</c>): each test forces the secondary roll to land
/// (<see cref="ForceSecondaryRules"/>) so only the immunity gate can be what blocks it. Repo audit
/// 2026-07-19: before the gate covered Standard/Drain, Body Slam could paralyze a Ghost off a
/// 0-damage hit. Jump Kick's crash-on-immunity carve-out is pinned in
/// <see cref="CrashDamageContractTests"/>; the pure-status seam (Thunder Wave vs Ground) in the
/// status contracts.
/// </summary>
[Collection(MovesCollection.Name)]
public class TypeImmunityContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("body-slam", DamageType.Ghost)] // Normal → Ghost 0×; 30% paralysis must not land
    [InlineData("thunder", DamageType.Ground)] // Electric → Ground 0×; 10% paralysis must not land
    [InlineData("lick", DamageType.Psychic)] // Ghost → Psychic 0× (the Gen 1 bug); 30% paralysis must not land
    public async Task ImmuneTargetTakesNoSecondaryStatus(string moveName, DamageType defenderType)
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("D", type1: defenderType, hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Has<StatusApplied>());
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);
    }

    [Fact]
    public async Task ImmuneTargetTakesNoSecondaryStatDrop()
    {
        // Constrict's Speed drop (33% in Gen 1; 10% is the modern value), forced to land — the
        // Ghost's stage must still not move.
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500))
            .Use(Move("constrict"));

        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Has<StatStageChanged>());
        Assert.Equal(0, result.Defender.Battle.Stages.Speed);
    }

    [Fact]
    public async Task ImmuneTargetIsNotFlinched()
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance)
            .Defender(TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500))
            .Use(Move("bite")); // Normal in Gen 1, 10% flinch forced to land

        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Defender.Battle.IsFlinched);
    }

    [Fact]
    public async Task RecoilMoveIntoImmuneTargetHurtsNoOne()
    {
        var attacker = TestCreatures.Make("A", hp: 300);
        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500))
            .Use(Move("take-down"));

        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Has<RecoilDamage>());
        Assert.Equal(300, attacker.Attributes.HP);
    }

    [Fact]
    public async Task ImmuneHitAnnouncesNoEffectInsteadOfZeroDamage()
    {
        // The halt replaces the old fold-to-zero: an immune hit emits MoveHadNoEffect and no
        // DamageDealt at all (previously the client got a DamageDealt with Damage 0).
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500))
            .Use(Move("body-slam"));

        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(500, result.Defender.Attributes.HP);
    }

    [Fact]
    public async Task StruggleAgainstGhostHasNoEffectAndNoRecoil()
    {
        // Gen 1: Struggle is Normal-type, so a Ghost is immune and the user takes no recoil (the move
        // stays Normal through Gen 3 and goes typeless in Gen 4 — that gen's chart treatment of the
        // typeless move is what neutralizes this gate, not a rule change).
        // Struggle is the null-move path, so this drives AttackAction directly (no MoveScenario).
        var attacker = TestCreatures.Make("A", hp: 300);
        var defender = TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500);
        var emitter = new RecordingEmitter();

        await new AttackAction(
            attacker,
            defender,
            selectedMove: null, // Struggle
            Gen1TypeChart.Instance,
            AlwaysHitRules.Instance,
            emitter
        ).ExecuteAsync();

        Assert.Contains(emitter.Events, e => e is MoveHadNoEffect);
        Assert.DoesNotContain(emitter.Events, e => e is RecoilDamage);
        Assert.Equal(300, attacker.Attributes.HP);
        Assert.Equal(500, defender.Attributes.HP);
    }
}
