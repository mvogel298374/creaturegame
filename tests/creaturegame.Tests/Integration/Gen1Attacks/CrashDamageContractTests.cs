using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Crash-damage moves (Jump Kick): when the move <b>misses</b>, the user takes crash damage and the
/// target is untouched; on a hit there is no crash and normal damage applies. The crash amount comes
/// from <c>IBattleRules.CalculateCrashDamage</c> (Gen 1 = a flat 1 HP), so the magnitude is a gen
/// rule rather than a literal in the engine.
/// </summary>
[Collection(MovesCollection.Name)]
public class CrashDamageContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("jump-kick")]
    [InlineData("high-jump-kick")]
    public async Task UserTakesCrashDamageOnMiss(string moveName)
    {
        var attacker = TestCreatures.Make("A", hp: 300);
        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Rules(NeverHitRules.Instance)
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(result.Defender.Attributes.MaxHP, result.Defender.Attributes.HP); // target untouched

        int expected = Gen1BattleRules.Instance.CalculateCrashDamage(result.Attacker);
        var crash = result.First<CrashDamage>();
        Assert.NotNull(crash);
        Assert.Equal(expected, crash!.Damage);
        Assert.Equal(result.Attacker.Attributes.MaxHP - expected, result.Attacker.Attributes.HP);
        Assert.Equal(result.Attacker.Attributes.HP, crash.HpAfter);
    }

    [Theory]
    [InlineData("jump-kick")]
    [InlineData("high-jump-kick")]
    public async Task UserTakesCrashDamageOnTypeImmunity(string moveName)
    {
        // Gen 1: a Fighting Jump Kick at a Ghost (0×) crashes the user just like an accuracy miss —
        // the move connects (AlwaysHit) but the type immunity, not a whiff, is what fails it.
        var attacker = TestCreatures.Make("A", hp: 300, attack: 200);
        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", type1: DamageType.Ghost, hp: 500))
            .Use(Move(moveName)); // default AlwaysHitRules ⇒ the failure is the immunity, not a miss

        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.False(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
        Assert.Equal(result.Defender.Attributes.MaxHP, result.Defender.Attributes.HP); // target untouched

        int expected = Gen1BattleRules.Instance.CalculateCrashDamage(result.Attacker);
        var crash = result.First<CrashDamage>();
        Assert.NotNull(crash);
        Assert.Equal(expected, crash!.Damage);
        Assert.Equal(result.Attacker.Attributes.MaxHP - expected, result.Attacker.Attributes.HP);
        Assert.Equal(result.Attacker.Attributes.HP, crash.HpAfter);
    }

    [Theory]
    [InlineData("jump-kick")]
    [InlineData("high-jump-kick")]
    public async Task NoCrashDamageOnHit(string moveName)
    {
        var attacker = TestCreatures.Make("A", hp: 300, attack: 200);
        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 9999, defense: 80))
            .Use(Move(moveName)); // default AlwaysHitRules ⇒ connects

        Assert.True(result.Has<DamageDealt>());
        Assert.False(result.Has<CrashDamage>());
        Assert.Equal(result.Attacker.Attributes.MaxHP, result.Attacker.Attributes.HP); // user unharmed
    }
}
