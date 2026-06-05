using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Never-miss moves (Swift) bypass the accuracy roll entirely — they connect even when the
/// generation's accuracy formula would always miss. The mechanic is honored in <c>AttackAction</c>
/// (the <c>NeverMisses</c> short-circuit before the accuracy check); this pins it over the real
/// imported Swift row by forcing the roll to fail and asserting the move still lands.
/// </summary>
[Collection(MovesCollection.Name)]
public class NeverMissContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task SwiftHitsEvenWhenTheAccuracyRollAlwaysFails()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance) // every accuracy roll fails — a normal move would miss
            .Defender(TestCreatures.Make("Defender", hp: 500, defense: 80))
            .Use(Move("swift"));

        Assert.False(result.Has<MoveMissed>());
        Assert.True(result.Has<DamageDealt>());
        Assert.True(result.TotalDamage > 0);
    }
}
