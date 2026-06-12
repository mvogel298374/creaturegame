using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Binding moves (Bind, Wrap, Clamp, Fire Spin, …) deal damage and trap the target for 2–5 turns. Gen 1 deals
/// NO end-of-turn residual chip — the per-turn damage is the binder re-using the move (the binder is locked in;
/// see <c>BindingMechanic</c> / <c>BindingInteractionTests</c>). This contract pins that a single use deals
/// damage, starts the trap, and sets the victim's turn counter.
/// </summary>
[Collection(MovesCollection.Name)]
public class BindingContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("bind")]
    [InlineData("wrap")]
    [InlineData("fire-spin")]
    [InlineData("clamp")]
    public async Task DealsDamageAndStartsBinding(string moveName)
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 9999, defense: 80))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>());
        Assert.True(result.TotalDamage > 0);

        var binding = result.First<BindingStarted>();
        Assert.NotNull(binding);
        Assert.Equal(result.Defender.Name, binding!.TargetName);
        Assert.InRange(result.Defender.Battle.BindingTurnsRemaining, 2, 5);
    }
}
