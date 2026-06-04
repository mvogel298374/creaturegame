using System.Linq;
using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Counter returns double the damage the user last took from a Normal/Fighting move. Gen 1 stores
/// that "last damage" until overwritten, and Counter's −5 priority makes it resolve after the
/// opponent's hit — so it answers this turn's attack. It fails (no damage) when the last hit was a
/// different type or there was none. The move-level tests pin the maths; the full-Battle test proves
/// the −5 priority + last-damage tracking line up in real play.
/// </summary>
[Collection(MovesCollection.Name)]
public class CounterContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData(DamageType.Normal)]
    [InlineData(DamageType.Fighting)]
    public async Task CounterReturnsDoubleTheLastNormalOrFightingDamage(DamageType lastType)
    {
        var attacker = TestCreatures.Make("A");
        attacker.LastDamageTaken = 50;
        attacker.LastDamageType  = lastType;

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        var hit = result.First<DamageDealt>();
        Assert.NotNull(hit);
        Assert.Equal(100, hit!.Damage);
        Assert.Equal("D", hit.TargetName);
    }

    [Fact]
    public async Task CounterFailsAgainstNonPhysicalTypeDamage()
    {
        var attacker = TestCreatures.Make("A");
        attacker.LastDamageTaken = 50;
        attacker.LastDamageType  = DamageType.Water;   // Gen 1 Counter only answers Normal/Fighting

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task CounterFailsWhenNoDamageWasTaken()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("counter"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Has<DamageDealt>());
    }

    [Fact]
    public async Task CounterAnswersTheOpponentsPhysicalHitInAFullBattle()
    {
        // Enemy spams a Normal physical move; Counter (−5) resolves after it and returns 2× the
        // damage the player took. Player is bulky enough to keep countering until the enemy faints.
        var player = TestCreatures.Make("Player", hp: 9999, defense: 100, speed: 1);
        player.AddAttack(Move("counter"));
        var enemy = TestCreatures.Make("Enemy", hp: 400, attack: 100, speed: 200);
        enemy.AddAttack(Move("tackle"));

        var emitter = new RecordingEmitter();
        var battle = new Battle(player, enemy, Gen1TypeChart.Instance, AutoSelectInput.Instance,
                                AutoSelectInput.Instance, rules: AlwaysHitRules.Instance, emitter: emitter);
        await battle.StartFightAsync();

        var tackleHits  = emitter.Events.OfType<DamageDealt>().Where(d => d.TargetName == "Player").ToList();
        var counterHits = emitter.Events.OfType<DamageDealt>().Where(d => d.TargetName == "Enemy").ToList();
        Assert.NotEmpty(tackleHits);
        Assert.NotEmpty(counterHits);
        Assert.Equal(tackleHits[0].Damage * 2, counterHits[0].Damage);   // first counter = 2× first tackle
    }
}
