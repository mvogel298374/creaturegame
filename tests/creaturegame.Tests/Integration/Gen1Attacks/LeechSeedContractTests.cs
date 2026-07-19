using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Leech Seed plants a seed on hit (no damage), then each end-of-turn drains 1/16 of the seeded
/// creature's max HP and heals the seeder by the same amount. Application is tested at the
/// <c>AttackAction</c> level; the per-turn drain/heal lives in <c>Battle</c> (it needs to see both
/// creatures), so it's proven in a full battle. The Grass-type immunity lives in
/// <see cref="ImmunityContractTests"/>.
/// </summary>
[Collection(MovesCollection.Name)]
public class LeechSeedContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task SeedsTheTargetOnHitWithoutDamage()
    {
        var result = await new MoveScenario()
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move("leech-seed"));

        Assert.True(result.Defender.Battle.HasLeechSeed);
        Assert.Contains(result.Events, e => e is LeechSeedApplied);
        Assert.False(result.Has<DamageDealt>(), "Leech Seed is a status move — no direct damage");
    }

    [Fact]
    public async Task DoesNotSeedOnMiss()
    {
        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Defender(TestCreatures.Make("D", type1: DamageType.Water, hp: 500))
            .Use(Move("leech-seed"));

        Assert.True(result.Has<MoveMissed>());
        Assert.False(result.Defender.Battle.HasLeechSeed);
    }

    [Fact]
    public async Task DrainsTheTargetAndHealsTheSeederEachTurn()
    {
        // Full battle: player seeds the enemy and out-speeds it; the enemy only Splashes (no damage),
        // so every end-of-turn the enemy loses 1/16 of its max HP and the player heals that much,
        // until the enemy is drained to faint. Player starts at 1 HP so the heal is unmistakable.
        var player = TestCreatures.Make("Player", type1: DamageType.Grass, hp: 400, speed: 200);
        player.Attributes.HP = 1;
        player.AddAttack(Move("leech-seed"));

        var enemy = TestCreatures.Make(
            "Enemy",
            type1: DamageType.Water,
            hp: 80,
            defense: 200,
            speed: 1
        );
        enemy.AddAttack(
            new Attack
            {
                Name = "Splash",
                BaseDamage = 0,
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
            rules: AlwaysHitRules.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        var drain = emitter
            .Events.OfType<LeechSeedDamage>()
            .FirstOrDefault(d => d.DrainedName == "Enemy");
        var heal = emitter
            .Events.OfType<LeechSeedHealed>()
            .FirstOrDefault(h => h.HealedName == "Player");
        Assert.NotNull(drain);
        Assert.NotNull(heal);
        Assert.Equal(80 / 16, drain!.Damage); // 1/16 of the seeded creature's max HP
        Assert.Equal(drain.Damage, heal!.Amount);
    }

    [Fact]
    public async Task DrainReadsItsOwnSeamMemberNotThePoisonDenominator()
    {
        // The drain divisor is LeechSeedDrainDenominator, NOT PoisonDamageDenominator — numerically
        // equal in Gen 1, but they diverge in Gen 2 (drain 1/8, poison still 1/16), so the drain
        // must not borrow the poison member. Override only the leech member and assert the drain
        // follows it while the poison rate is untouched.
        var player = TestCreatures.Make("Player", type1: DamageType.Grass, hp: 400, speed: 200);
        player.AddAttack(Move("leech-seed"));

        var enemy = TestCreatures.Make(
            "Enemy",
            type1: DamageType.Water,
            hp: 80,
            defense: 200,
            speed: 1
        );
        enemy.AddAttack(
            new Attack
            {
                Name = "Splash",
                BaseDamage = 0,
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
            rules: new LeechEightPoisonSixteenRules(),
            emitter: emitter
        );
        await battle.StartFightAsync();

        var drain = emitter
            .Events.OfType<LeechSeedDamage>()
            .FirstOrDefault(d => d.DrainedName == "Enemy");
        Assert.NotNull(drain);
        Assert.Equal(80 / 8, drain!.Damage); // follows the overridden leech member (1/8), not poison (1/16)
    }

    /// <summary>Always-hit rules with the leech drain at 1/8 while poison stays 1/16 — the Gen 2 split.</summary>
    private sealed class LeechEightPoisonSixteenRules : DelegatingBattleRules
    {
        public override int LeechSeedDrainDenominator => 8;

        public override int GetHitThreshold(int acc, int accStage, int evaStage) => 256;
    }
}
