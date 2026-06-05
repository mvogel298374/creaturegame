using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Self-heal moves (Recover) restore a fraction of the user's max HP and deal no damage. The fraction
/// is the gen-variable <see cref="IBattleRules.RecoverHealFraction"/> (Gen 1 = ½); healing is capped at
/// max HP. Soft-Boiled joins this class in its batch.
/// </summary>
[Collection(MovesCollection.Name)]
public class HealContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task RecoverRestoresHalfOfMaxHp()
    {
        var healer = TestCreatures.Make("Healer", hp: 200);
        healer.Attributes.HP = 50;     // damaged

        var result = await new MoveScenario()
            .Attacker(healer)
            .Use(Move("recover"));

        Assert.False(result.Has<DamageDealt>(), "Recover is a status move — no damage");
        Assert.Equal(150, healer.Attributes.HP);   // 50 + ½ × 200

        var healed = result.First<Healed>();
        Assert.NotNull(healed);
        Assert.Equal("Healer", healed!.CreatureName);
        Assert.Equal(100, healed.HealAmount);
        Assert.Equal(150, healed.HpAfter);
    }

    [Fact]
    public async Task RecoverDoesNotOverhealPastMaxHp()
    {
        var healer = TestCreatures.Make("Healer", hp: 200);
        healer.Attributes.HP = 180;    // ½ × 200 would overshoot

        var result = await new MoveScenario()
            .Attacker(healer)
            .Use(Move("recover"));

        Assert.Equal(200, healer.Attributes.HP);   // capped at max, not 280
        Assert.True(result.Has<Healed>());
    }
}
