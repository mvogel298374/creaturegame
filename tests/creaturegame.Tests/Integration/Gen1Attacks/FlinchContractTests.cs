using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Flinch moves (Stomp, Rolling Kick, Headbutt, …) set the target's flinch flag on hit, never on a
/// miss. Flinch only matters across the turn loop — a flinched battler that hasn't moved yet loses
/// its turn — so the third test drives a full <see cref="Battle"/> with a faster flincher to prove
/// the flag is actually consumed in real play (set in <c>AttackAction</c>, read in
/// <c>StatusResolver.CanAct</c>), not just stored.
/// </summary>
[Collection(MovesCollection.Name)]
public class FlinchContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Theory]
    [InlineData("stomp")]
    [InlineData("rolling-kick")]
    [InlineData("headbutt")]
    [InlineData("bite")]
    [InlineData("low-kick")]
    [InlineData("bone-club")]
    public async Task SetsFlinchFlagOnHit(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(ForceSecondaryRules.Instance) // forces the secondary chance to land
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<DamageDealt>());
        Assert.True(result.Defender.IsFlinched);
    }

    [Theory]
    [InlineData("stomp")]
    [InlineData("rolling-kick")]
    [InlineData("headbutt")]
    [InlineData("bite")]
    [InlineData("low-kick")]
    [InlineData("bone-club")]
    public async Task NoFlinchOnMiss(string moveName)
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("Defender", hp: 500))
            .Use(Move(moveName));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Defender.IsFlinched);
    }

    [Fact]
    public async Task FasterFlincherMakesTargetLoseItsTurn()
    {
        // Player outspeeds and Stomps every turn with flinch forced to land. The enemy is flinched
        // before it can act, so it never gets a move off — assert FlinchBlocked fires and the enemy
        // never emits MoveUsed across the whole battle. The enemy is tanky enough to survive several
        // turns (so it actually reaches its blocked action) but still faints eventually.
        var player = TestCreatures.Make("Player", attack: 80, speed: 200);
        player.AddAttack(Move("stomp"));

        var enemy = TestCreatures.Make("Enemy", hp: 300, defense: 100, speed: 1);
        enemy.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: ForceSecondaryRules.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        Assert.Contains(emitter.Events, e => e is FlinchBlocked f && f.CreatureName == "Enemy");
        Assert.DoesNotContain(emitter.Events, e => e is MoveUsed m && m.AttackerName == "Enemy");
        Assert.False(enemy.IsAlive());
        Assert.True(player.IsAlive());
    }
}
