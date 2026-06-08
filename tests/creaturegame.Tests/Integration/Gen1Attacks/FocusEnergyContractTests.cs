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
}
