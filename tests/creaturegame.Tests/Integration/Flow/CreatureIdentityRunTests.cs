using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// Creature Identity Stage 1 (<c>ARCHITECTURE.md</c> §2.2): every creature that enters a run — starter, foes, drafted —
/// is identified by <see cref="RunDirector"/> with a unique, deterministic id, even when display names collide.
/// Drives a real run with delegate suppliers (no DB), like <see cref="RunDirectorAcquisitionTests"/>.
/// </summary>
public class CreatureIdentityRunTests
{
    private static Creature Mon(string name, int hp, int attack, int speed, int level)
    {
        var c = TestCreatures.Make(name, hp: hp, speed: speed, attack: attack);
        c.Level = level;
        c.AddAttack(
            new Attack
            {
                Name = "tackle",
                BaseDamage = 40,
                PowerPointsMax = 35,
                DamageType = DamageType.Normal,
                AttackType = AttackType.Physical,
            }
        );
        return c;
    }

    /// <summary>One win (a same-named foe), an accepted same-named draft, then a Bruiser that ends the run.
    /// Returns everything the run created, in creation order.</summary>
    private static async Task<(
        RunDirector runner,
        Creature starter,
        List<Creature> foes,
        Creature draftee
    )> RunOnceAsync()
    {
        var starter = Mon("PIDGEY", hp: 300, attack: 999, speed: 100, level: 50);
        var foes = new List<Creature>();
        var draftee = Mon("PIDGEY", hp: 100, attack: 50, speed: 50, level: 40);

        Func<Creature, int, BiomeDefinition?, EncounterTier, Task<Creature>> enemySupplier = (
            _,
            _,
            _,
            _
        ) =>
        {
            var foe =
                foes.Count == 0
                    ? Mon("PIDGEY", hp: 1, attack: 1, speed: 1, level: 5) // wild twin of the starter
                    : Mon("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            foe.SpeciesId = 100 + foes.Count;
            foe.SpeciesBaseExperience = 50;
            foes.Add(foe);
            return Task.FromResult(foe);
        };

        var input = new ScriptedInput("tackle").AcceptsAcquisition();
        var runner = new RunDirector(
            starter,
            enemySupplier,
            Gen1TypeChart.Instance,
            input,
            input,
            movePool: Array.Empty<Attack>(),
            new RunDirectorOptions
            {
                Emitter = new RecordingEmitter(),
                Rules = new ScriptableRules().Deterministic(),
                Rng = new SeededRandomSource(0),
                HealEveryNBattles = 0,
                DraftSupplier = (_, _) => Task.FromResult<Creature?>(draftee),
            }
        );
        await runner.RunAsync();
        return (runner, starter, foes, draftee);
    }

    [Fact]
    public async Task EveryCreatureThatEntersARun_GetsAUniqueId_EvenWhenNamesCollide()
    {
        var (runner, starter, foes, draftee) = await RunOnceAsync();

        var everyone = new[] { starter }.Concat(foes).Append(draftee).ToList();
        Assert.All(everyone, c => Assert.NotEqual(0, c.Id));
        Assert.Equal(everyone.Count, everyone.Select(c => c.Id).Distinct().Count());

        // The collision cases the wire has to survive: wild twin of the starter, and a drafted twin.
        Assert.Equal("PIDGEY", starter.Name);
        Assert.Equal("PIDGEY", foes[0].Name);
        Assert.Equal("PIDGEY", draftee.Name);
        Assert.Equal(everyone.Count, runner.State.Ids.HighWater); // no id minted that no creature holds
    }

    [Fact]
    public async Task TheStarterIsIdOne_AndIdsFollowEntryOrder()
    {
        var (_, starter, foes, draftee) = await RunOnceAsync();

        Assert.Equal(1, starter.Id);
        Assert.Equal(2, foes[0].Id); // first foe is built before the win that offers the draft
        Assert.Equal(3, draftee.Id);
        Assert.Equal(4, foes[1].Id);
    }

    [Fact]
    public async Task SameSeed_ReproducesTheSameIdAssignment()
    {
        var (_, s1, f1, d1) = await RunOnceAsync();
        var (_, s2, f2, d2) = await RunOnceAsync();

        var first = new[] { s1.Id }.Concat(f1.Select(f => f.Id)).Append(d1.Id).ToList();
        var second = new[] { s2.Id }.Concat(f2.Select(f => f.Id)).Append(d2.Id).ToList();

        Assert.All(first, id => Assert.NotEqual(0, id)); // equal runs of zeros would pass vacuously
        Assert.Equal(first, second);
    }
}
