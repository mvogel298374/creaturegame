using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// The evolution wiring in <see cref="BattleRunner"/>: after a win, the injected resolver is consulted and,
/// if it returns an outcome, the runner applies <see cref="Creature.EvolveTo"/>, seats the evolved learnset,
/// emits <see cref="CreatureEvolved"/>, and drives the evolved form's level-up move learning. The resolver is
/// stubbed here (the IEvolutionRules decision + DB resolution are tested separately) so this pins the
/// orchestration: that it fires after the win, mutates the player, and produces the right event stream.
/// </summary>
public class BattleRunnerEvolutionTests
{
    [Fact]
    public async Task Runner_AfterWin_AppliesResolvedEvolution_AndEmitsEventThenLearnsMove()
    {
        var player = Fighter("CHARMANDER", hp: 200, attack: 999, speed: 100, level: 50);
        player.SpeciesId = 4;

        // One pushover (a guaranteed win), then a bruiser that ends the run — so exactly one evolution check.
        int built = 0;
        Func<Creature, Task<Creature>> supplier = _ =>
        {
            built++;
            var enemy =
                built == 1
                    ? Fighter("Pushover", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50; // small award at L50 — no level-up
            return Task.FromResult(enemy);
        };

        // The evolved form (Charmeleon) + a learnset move available at the player's current level (50),
        // so the post-evolution learn step auto-learns it into a free slot.
        var charmeleon = new PokemonSpecies
        {
            Id = 5,
            Name = "charmeleon",
            BaseHP = 58,
            BaseAttack = 64,
            BaseDefense = 58,
            BaseSpecial = 80,
            BaseSpeed = 80,
            Type1 = DamageType.Fire,
            GrowthRate = GrowthRate.MediumSlow,
            BaseExperience = 142,
        };
        var evoMove = new Attack("flamethrower", "") { Id = 53, BaseDamage = 95 };
        var outcome = new EvolutionOutcome(charmeleon, [new LearnsetMove(50, evoMove)]);

        int checks = 0;
        Func<Creature, Task<EvolutionOutcome?>> checkEvolution = _ =>
            Task.FromResult<EvolutionOutcome?>(checks++ == 0 ? outcome : null);

        var recorder = new RecordingEmitter();
        var runner = new BattleRunner(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            checkEvolution: checkEvolution
        );

        await runner.RunAsync();

        // Evolution fired exactly once, carrying both forms for the sprite morph.
        var evolved = Assert.Single(recorder.Of<CreatureEvolved>());
        Assert.Equal(4, evolved.FromSpeciesId);
        Assert.Equal(5, evolved.ToSpeciesId);
        Assert.Equal("CHARMANDER", evolved.FromName);
        Assert.Equal("CHARMELEON", evolved.ToName);

        // The player was actually mutated into the new form.
        Assert.Equal(5, player.SpeciesId);
        Assert.Equal("CHARMELEON", player.Name);
        Assert.Equal(58, player.BaseHP);

        // The evolved form's level-50 move was learned, after the evolution event.
        var learned = Assert.Single(recorder.Of<MoveLearned>());
        Assert.Equal("flamethrower", learned.MoveName);
        Assert.Contains(player.MoveSet, m => m.Base.Id == 53);

        int evolvedAt = recorder.Events.ToList().IndexOf(evolved);
        int learnedAt = recorder.Events.ToList().IndexOf(learned);
        Assert.True(evolvedAt < learnedAt, "CreatureEvolved precedes the evolution move learn");
    }

    [Fact]
    public async Task Runner_NullResolver_BehavesAsPlainChain_NoEvolution()
    {
        var player = Fighter("PLAYER", hp: 200, attack: 999, speed: 100, level: 50);
        int built = 0;
        Func<Creature, Task<Creature>> supplier = _ =>
        {
            built++;
            var enemy =
                built == 1
                    ? Fighter("Pushover", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new BattleRunner(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0)
        );

        await runner.RunAsync();

        Assert.Empty(recorder.Of<CreatureEvolved>());
        Assert.Single(recorder.Of<RunEnded>());
    }

    private static Creature Fighter(string name, int hp, int attack, int speed, int level)
    {
        var c = new Creature(name)
        {
            Level = level,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(level);
        c.Attributes.MaxHP = hp;
        c.Attributes.HP = hp;
        c.Attributes.Attack = attack;
        c.Attributes.Speed = speed;
        c.AddAttack(
            new Attack
            {
                Name = "tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
                PowerPointsMax = 99,
            }
        );
        return c;
    }
}
