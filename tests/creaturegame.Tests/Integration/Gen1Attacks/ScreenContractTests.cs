using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Reflect and Light Screen halve incoming damage of the matching kind by doubling the holder's
/// defensive stat (Gen 1, factor on <see cref="IBattleRules.ScreenDefenseMultiplier"/>): Reflect vs
/// physical, Light Screen vs special — and the wrong screen does nothing. Crits ignore screens.
/// </summary>
[Collection(MovesCollection.Name)]
public class ScreenContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    private async Task<int> DamageTaken(string move, Action<Creature> setup, IBattleRules rules)
    {
        var defender = TestCreatures.Make("D", hp: 9999, defense: 100, special: 100);
        setup(defender);
        var result = await new MoveScenario()
            .Rules(rules)
            .Attacker(TestCreatures.Make("A", attack: 150, special: 150))
            .Defender(defender)
            .Use(Move(move));
        return result.TotalDamage;
    }

    [Fact]
    public async Task ReflectReducesPhysicalDamageButLightScreenDoesNot()
    {
        int normal = await DamageTaken("tackle", _ => { }, NoVarianceNoCritHitRules.Instance);
        int reflected = await DamageTaken(
            "tackle",
            d => d.HasReflect = true,
            NoVarianceNoCritHitRules.Instance
        );
        int lightScreen = await DamageTaken(
            "tackle",
            d => d.HasLightScreen = true,
            NoVarianceNoCritHitRules.Instance
        );

        Assert.True(reflected < normal, "Reflect reduces physical damage");
        Assert.Equal(normal, lightScreen); // Light Screen is the wrong screen for a physical hit
    }

    [Fact]
    public async Task LightScreenReducesSpecialDamageButReflectDoesNot()
    {
        int normal = await DamageTaken("water-gun", _ => { }, NoVarianceNoCritHitRules.Instance);
        int lightScreen = await DamageTaken(
            "water-gun",
            d => d.HasLightScreen = true,
            NoVarianceNoCritHitRules.Instance
        );
        int reflected = await DamageTaken(
            "water-gun",
            d => d.HasReflect = true,
            NoVarianceNoCritHitRules.Instance
        );

        Assert.True(lightScreen < normal, "Light Screen reduces special damage");
        Assert.Equal(normal, reflected); // Reflect is the wrong screen for a special hit
    }

    [Fact]
    public async Task CritsIgnoreReflect()
    {
        // On a crit the screen is bypassed (Gen 1), so damage matches the unscreened crit.
        int critNoScreen = await DamageTaken("tackle", _ => { }, AlwaysCritRules.Instance);
        int critReflect = await DamageTaken(
            "tackle",
            d => d.HasReflect = true,
            AlwaysCritRules.Instance
        );

        Assert.Equal(critNoScreen, critReflect);
    }

    [Fact]
    public async Task ReflectIsAnnouncedAndSetsTheFlag()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Use(Move("reflect"));

        Assert.True(result.Attacker.HasReflect);
        Assert.False(result.Has<DamageDealt>(), "Reflect is a status move — no damage");
        Assert.Contains(result.Events, e => e is ScreenApplied s && s.ScreenName == "Reflect");
    }

    [Fact]
    public async Task LightScreenIsAnnouncedAndSetsTheFlag()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Use(Move("light-screen"));

        Assert.True(result.Attacker.HasLightScreen);
        Assert.Contains(result.Events, e => e is ScreenApplied s && s.ScreenName == "Light Screen");
    }
}
