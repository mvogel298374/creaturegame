using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Focus Energy in Gen 1 is famously bugged: instead of quadrupling the critical-hit rate it
/// <b>quarters</b> it. The flag is set by the move and the bugged modifier lives in
/// <see cref="Gen1BattleRules.GetCritChance"/>. This pins the quirk, not just that a flag flips.
/// </summary>
[Collection(MovesCollection.Name)]
public class FocusEnergyContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task FocusEnergyIsAnnouncedAndSetsTheFlag()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Use(Move("focus-energy"));

        Assert.True(result.Attacker.Battle.HasFocusEnergy);
        Assert.False(result.Has<DamageDealt>(), "Focus Energy is a status move — no damage");
        Assert.Contains(result.Events, e => e is FocusEnergyApplied);
    }

    [Fact]
    public void FocusEnergyQuartersTheCritChanceInGen1()
    {
        var attacker = TestCreatures.Make("A", baseSpeed: 200);
        var move = Move("tackle"); // not a high-crit move

        double normal = Gen1BattleRules.Instance.GetCritChance(attacker, move);
        attacker.Battle.HasFocusEnergy = true;
        double focused = Gen1BattleRules.Instance.GetCritChance(attacker, move);

        Assert.True(focused < normal, "the Gen 1 bug lowers crit rate instead of raising it");
        // floor(floor(200/2)/4)/256 = floor(100/4)/256 = 25/256
        Assert.Equal(25.0 / 256.0, focused, 5);
    }

    [Theory]
    [InlineData(100, 100)] // loses less than three quarters of the capped 255
    [InlineData(130, 128)]
    [InlineData(255, 252)]
    [InlineData(30, 28)] // floor(30/4) x 4, below a plain quarter of 120
    public void HighCritMoveUnderFocusEnergy_HalvesBeforeTheMultiplier(int baseSpeed, int numerator)
    {
        var attacker = TestCreatures.Make("A", baseSpeed: baseSpeed);
        attacker.Battle.HasFocusEnergy = true;

        double chance = Gen1BattleRules.Instance.GetCritChance(
            attacker,
            new Attack { Name = "Slash", IsHighCrit = true }
        );

        Assert.Equal(numerator / 256.0, chance);
    }

    [Theory]
    [InlineData(100, true, 255)]
    [InlineData(64, true, 255)]
    [InlineData(63, true, 248)]
    [InlineData(31, true, 120)]
    [InlineData(100, false, 50)]
    [InlineData(31, false, 15)]
    public void CritChanceWithoutFocusEnergy_MatchesPokered(
        int baseSpeed,
        bool isHighCrit,
        int numerator
    )
    {
        var attacker = TestCreatures.Make("A", baseSpeed: baseSpeed);

        double chance = Gen1BattleRules.Instance.GetCritChance(
            attacker,
            new Attack { Name = "Move", IsHighCrit = isHighCrit }
        );

        Assert.Equal(numerator / 256.0, chance);
    }

    [Theory]
    [InlineData(200, 25)]
    [InlineData(100, 12)]
    [InlineData(15, 1)]
    public void NormalMoveUnderFocusEnergy_MatchesPokered(int baseSpeed, int numerator)
    {
        var attacker = TestCreatures.Make("A", baseSpeed: baseSpeed);
        attacker.Battle.HasFocusEnergy = true;

        double chance = Gen1BattleRules.Instance.GetCritChance(
            attacker,
            new Attack { Name = "Tackle", IsHighCrit = false }
        );

        Assert.Equal(numerator / 256.0, chance);
    }
}
