using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Bide commits the user for 2–3 turns, storing the damage it takes, then unleashes double that total
/// at the foe (typeless, never misses). The first test drives the storing turn through the real
/// <c>AttackAction</c>; the second runs a full <see cref="Battle"/> to prove the lock-in (input asked
/// once), the accumulation, and the 2× release — the quirk, not just "it dealt damage".
/// </summary>
[Collection(MovesCollection.Name)]
public class BideContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task BideStoresWithoutAttackingOnTheFirstTurn()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", hp: 500))
            .Use(Move("bide"));

        Assert.True(result.Has<BideStoring>());
        Assert.False(result.Has<DamageDealt>(), "Bide deals no damage while storing");
        Assert.True(result.Attacker.BideTurnsRemaining > 0, "the user is committed to Bide");
    }

    [Fact]
    public async Task BideUnleashesDoubleTheDamageAbsorbed()
    {
        // 2-turn Bide: store turn 1 (absorbing the enemy's hit), unleash turn 2. The player outspeeds,
        // so on the release turn it unleashes before the enemy acts — felling the 1-HP enemy. The
        // released damage must equal 2× the total the player absorbed while storing.
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 2000;
        player.Attributes.Speed = 200;
        player.AddAttack(Move("bide"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 1;   // any non-zero unleash fells it
        enemy.Attributes.Attack = 60;
        enemy.Attributes.Speed  = 1;
        enemy.AddAttack(Move("tackle"));

        var emitter = new RecordingEmitter();
        var input   = new CountingInput(0);
        var battle  = new Battle(player, enemy, Gen1TypeChart.Instance, input, AutoSelectInput.Instance,
                                 rules: new FixedBideRules(), emitter: emitter);
        await battle.StartFightAsync();

        Assert.Contains(emitter.Events, e => e is BideStoring);
        Assert.Equal(1, input.CallCount);   // asked only on the first turn — Bide auto-repeats while committed
        Assert.False(enemy.IsAlive());

        int absorbed = emitter.Of<DamageDealt>().Where(d => d.TargetName == "Player").Sum(d => d.Damage);
        var unleashed = emitter.Of<DamageDealt>().First(d => d.TargetName == "Enemy");
        Assert.True(absorbed > 0, "the player absorbed a hit while storing");
        Assert.Equal(absorbed * 2, unleashed.Damage);
    }

    [Fact]
    public async Task BideAbsorbsNonStandardDamageToo()
    {
        // Bide stores damage from any category, not just normal attacks. The enemy uses Sonic Boom
        // (fixed 20) while the player stores; the release must be exactly 2 × 20 = 40.
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 2000;
        player.Attributes.Speed = 200;
        player.AddAttack(Move("bide"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(Move("sonic-boom"));   // fixed 20 damage (DamageCategory.Fixed)

        var emitter = new RecordingEmitter();
        var battle  = new Battle(player, enemy, Gen1TypeChart.Instance,
                                 new CountingInput(0), AutoSelectInput.Instance,
                                 rules: new FixedBideRules(), emitter: emitter);
        await battle.StartFightAsync();

        var unleashed = emitter.Of<DamageDealt>().First(d => d.TargetName == "Enemy");
        Assert.Equal(40, unleashed.Damage);   // 2 × the fixed 20 absorbed while storing
    }

    /// <summary>Always-hit, no-crit, no-variance, and a fixed 2-turn Bide for determinism.</summary>
    private sealed class FixedBideRules : DelegatingBattleRules
    {
        public override int    RollBideTurns()                                       => 2;
        public override int    GetHitThreshold(int acc, int accStage, int evaStage)  => 256;
        public override double GetCritChance(Creature a, Attack m)                   => 0.0;
        public override double RollDamageVariance()                                  => 1.0;
    }

    private sealed class CountingInput(int index) : IBattleInput
    {
        public int CallCount { get; private set; }
        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
        {
            CallCount++;
            return Task.FromResult(context.Attacker.MoveSet[index]);
        }
    }
}
