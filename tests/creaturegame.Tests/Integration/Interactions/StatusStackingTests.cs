using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Interactions;

/// <summary>
/// Full-battle probes for how status conditions stack (or don't): Gen 1 allows only one *major* status
/// at a time, but confusion is a separate volatile track that coexists with one — and independent
/// end-of-turn drains (major-status damage + Leech Seed) both bite the same creature each turn.
/// </summary>
[Collection(MovesCollection.Name)]
public class StatusStackingTests(MovesFixture moves) : InteractionTest(moves)
{
    // Gen 1: a Pokémon can hold only one major status. Once poisoned, a sleep move can't also put it
    // to sleep — Spore fails and the poison stands.
    [Fact]
    public async Task APoisonedCreatureCannotAlsoBePutToSleep()
    {
        var enemy = Mon(
            "Enemy",
            hp: 999,
            attack: 5,
            speed: 200,
            DamageType.Normal,
            "poison-powder",
            "spore"
        );
        var player = Mon("Player", hp: 160, attack: 5, speed: 1, DamageType.Normal, "splash");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .EnemyUses("poison-powder", "spore")
            .PlayerUses("splash")
            .RunAsync();

        // Poison landed, sleep never did, and the player stayed poisoned (eventually fainting to it).
        Assert.Contains(
            result.All<StatusApplied>(),
            s => s.TargetName == "Player" && s.Status == StatusCondition.Poison
        );
        Assert.DoesNotContain(
            result.All<StatusApplied>(),
            s => s.TargetName == "Player" && s.Status == StatusCondition.Sleep
        );
    }

    // Gen 1: confusion is independent of major status — a paralysed Pokémon can still be confused
    // (Confuse Ray succeeds on top of the paralysis). Player is Water so it's immune to neither the
    // Electric Thunder Wave nor the Ghost Confuse Ray.
    [Fact]
    public async Task ConfusionCoexistsWithParalysis()
    {
        var enemy = Mon(
            "Enemy",
            hp: 999,
            attack: 80,
            speed: 200,
            DamageType.Normal,
            "thunder-wave",
            "confuse-ray",
            "tackle"
        );
        var player = Mon("Player", hp: 200, attack: 5, speed: 1, DamageType.Water, "splash");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .EnemyUses("thunder-wave", "confuse-ray", "tackle")
            .PlayerUses("splash")
            .RunAsync();

        // Paralysis applied, then Confuse Ray still took hold on top of it.
        Assert.Contains(
            result.All<StatusApplied>(),
            s => s.TargetName == "Player" && s.Status == StatusCondition.Paralysis
        );
        Assert.Contains(result.All<ConfusionStarted>(), c => c.TargetName == "Player");
    }

    // Gen 1: a major status and Leech Seed are independent end-of-turn drains — a poisoned, seeded
    // creature is bitten by BOTH every turn (poison tick + seed drain), and the seeder is healed by the
    // drain. The two damage sources coexist rather than one overriding the other.
    [Fact]
    public async Task PoisonAndLeechSeedBothDrainTheTargetEachTurn()
    {
        var player = Mon(
            "Player",
            hp: 999,
            attack: 5,
            speed: 200,
            DamageType.Normal,
            "leech-seed",
            "poison-powder",
            "splash"
        );
        var enemy = Mon("Enemy", hp: 400, attack: 5, speed: 1, DamageType.Normal, "splash");

        var result = await new BattleScenario()
            .Player(player)
            .Enemy(enemy)
            .PlayerUses("leech-seed", "poison-powder", "splash")
            .EnemyUses("splash")
            .RunAsync();

        // Both independent drains land on the enemy…
        Assert.Contains(
            result.All<StatusDamage>(),
            d => d.TargetName == "Enemy" && d.Source == StatusCondition.Poison
        );
        Assert.Contains(result.All<LeechSeedDamage>(), d => d.DrainedName == "Enemy");
        // …and the seed feeds the player (proving the seed half ran, not just the poison).
        Assert.Contains(result.All<LeechSeedHealed>(), h => h.HealedName == "Player");
    }
}
