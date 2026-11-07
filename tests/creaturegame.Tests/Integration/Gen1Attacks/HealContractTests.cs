using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Self-heal moves (Recover) restore a fraction of the user's max HP and deal no damage. The fraction
/// is the gen-variable <see cref="IBattleRules.RecoverHealFraction"/> (Gen 1 = ½); healing is capped at
/// max HP. The fail check is <see cref="IBattleRules.HealFails"/>.
/// </summary>
[Collection(MovesCollection.Name)]
public class HealContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    // Recover and Soft-Boiled share the same Heal mechanic (½ max HP).
    [Theory]
    [InlineData("recover")]
    [InlineData("soft-boiled")]
    public async Task RestoresHalfOfMaxHp(string moveName)
    {
        var healer = TestCreatures.Make("Healer", hp: 200);
        healer.Attributes.HP = 50; // damaged

        var result = await new MoveScenario().Attacker(healer).Use(Move(moveName));

        Assert.False(result.Has<DamageDealt>(), $"{moveName} is a status move — no damage");
        Assert.Equal(150, healer.Attributes.HP); // 50 + ½ × 200

        var healed = result.First<Healed>();
        Assert.NotNull(healed);
        Assert.Equal("Healer", healed!.CreatureName);
        Assert.Equal(100, healed.HealAmount);
        Assert.Equal(150, healed.HpAfter);
    }

    [Theory]
    [InlineData("recover")]
    [InlineData("soft-boiled")]
    public async Task DoesNotOverhealPastMaxHp(string moveName)
    {
        var healer = TestCreatures.Make("Healer", hp: 200);
        healer.Attributes.HP = 180; // ½ × 200 would overshoot

        var result = await new MoveScenario().Attacker(healer).Use(Move(moveName));

        Assert.Equal(200, healer.Attributes.HP); // capped at max, not 280
        Assert.True(result.Has<Healed>());
    }

    // GENERATION_SEAMS.md §2, Heal failure.
    [Theory]
    [InlineData("recover", 600, 600)]
    [InlineData("recover", 600, 345)]
    [InlineData("recover", 600, 89)]
    [InlineData("recover", 300, 45)]
    [InlineData("soft-boiled", 600, 600)]
    [InlineData("soft-boiled", 600, 345)]
    [InlineData("soft-boiled", 600, 89)]
    [InlineData("soft-boiled", 300, 45)]
    public async Task FailsAtFullHpAndAtTheLowByteBugGaps(string moveName, int maxHp, int hp)
    {
        var healer = TestCreatures.Make("Healer", hp: maxHp);
        healer.Attributes.HP = hp;

        var result = await new MoveScenario().Attacker(healer).Use(Move(moveName));

        Assert.True(result.Has<MoveFailed>());
        Assert.False(result.Has<Healed>());
        Assert.False(result.Has<MoveMissed>());
        Assert.Equal(hp, healer.Attributes.HP);
        Assert.Equal(result.Move.Base.PowerPointsMax - 1, result.Move.PowerPointsCurrent);
    }

    [Theory]
    [InlineData("recover", 600, 346)]
    [InlineData("recover", 511, 256)]
    [InlineData("soft-boiled", 600, 346)]
    [InlineData("soft-boiled", 511, 256)]
    public async Task HealsOutsideTheBugGaps(string moveName, int maxHp, int hp)
    {
        var healer = TestCreatures.Make("Healer", hp: maxHp);
        healer.Attributes.HP = hp;

        var result = await new MoveScenario().Attacker(healer).Use(Move(moveName));

        Assert.False(result.Has<MoveFailed>());
        Assert.Equal(maxHp - hp, result.First<Healed>()!.HealAmount);
        Assert.Equal(maxHp, healer.Attributes.HP);
    }
}
