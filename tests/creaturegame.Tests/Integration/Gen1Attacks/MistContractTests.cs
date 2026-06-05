using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Mist shrouds the user so the opponent can't lower its stats (Gen 1: until the battle ends — the
/// flag lives on <c>BattleState</c> and clears on the per-battle reset). It deals no damage, blocks
/// foe-induced stat drops on the holder, and leaves the holder's own stat changes (and any raises)
/// untouched. The flag is read during stat application, so move-level coverage exercises the real path.
/// </summary>
[Collection(MovesCollection.Name)]
public class MistContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task MistShroudsTheUserWithoutDealingDamage()
    {
        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("A"))
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("mist"));

        Assert.False(result.Has<DamageDealt>(), "Mist is a status move — no damage");
        Assert.True(result.Attacker.HasMist);
        var applied = result.First<MistApplied>();
        Assert.NotNull(applied);
        Assert.Equal(result.Attacker.Name, applied!.CreatureName);
    }

    [Fact]
    public async Task MistBlocksAFoeInducedStatDrop()
    {
        // The defender is already shrouded in Mist; the attacker's Growl (−1 foe Attack) is blocked.
        var defender = TestCreatures.Make("Defender", hp: 500);
        defender.HasMist = true;

        var result = await new MoveScenario().Defender(defender).Use(Move("growl"));

        Assert.Equal(0, result.Defender.Stages.Attack); // not lowered
        Assert.False(result.Has<StatStageChanged>());
        Assert.Contains(result.Events, e => e is StatDropBlocked s && s.CreatureName == "Defender");
    }

    [Fact]
    public async Task MistDoesNotBlockTheHoldersOwnStatBuff()
    {
        // Mist only blocks the opponent's reductions — a self-targeting raise still applies.
        var attacker = TestCreatures.Make("A");
        attacker.HasMist = true;

        var result = await new MoveScenario().Attacker(attacker).Use(Move("swords-dance"));

        Assert.Equal(2, result.Attacker.Stages.Attack);
        Assert.False(result.Has<StatDropBlocked>());
    }
}
