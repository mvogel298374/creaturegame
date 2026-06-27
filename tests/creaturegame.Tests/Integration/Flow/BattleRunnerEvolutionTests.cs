using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Evolution;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// The evolution wiring in <see cref="BattleRunner"/>: after a win that <b>levels the player up</b> (Gen 1
/// attempts evolution on level-up), the injected resolver is consulted; the player is then offered the
/// evolution and can allow or cancel it. The resolver is stubbed here (the IEvolutionRules decision + DB
/// resolution are tested separately) so this pins the orchestration — the level-up gate, the offer→decision
/// flow, the apply/learn on allow, and the no-op on cancel.
/// </summary>
public class BattleRunnerEvolutionTests
{
    // Charmander(4) → Charmeleon(5); a learnset move at level 6 (the level the player reaches below), so the
    // post-evolution learn step auto-learns it into a free slot.
    private static (PokemonSpecies Form, EvolutionOutcome Outcome) Charmeleon()
    {
        var form = new PokemonSpecies
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
        return (form, new EvolutionOutcome(form, [new LearnsetMove(6, evoMove)]));
    }

    // A win that levels a level-5 MediumFast player to exactly level 6: floor(199 * 5 / 7) = 142 XP, and
    // exp(5)=125, exp(6)=216, exp(7)=343 → 267 lands on 6. Then a bruiser ends the run (one evolution check).
    private static Func<Creature, int, Task<Creature>> WinThenLose()
    {
        int built = 0;
        return (_, _) =>
        {
            built++;
            var enemy =
                built == 1
                    ? Fighter("Pushover", hp: 1, attack: 1, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = built == 1 ? 199 : 50;
            return Task.FromResult(enemy);
        };
    }

    private static BattleRunner BuildRunner(
        Creature player,
        IBattleInput playerInput,
        RecordingEmitter recorder,
        Func<Creature, Task<EvolutionOutcome?>> checkEvolution
    ) =>
        new(
            player,
            WinThenLose(),
            Gen1TypeChart.Instance,
            playerInput,
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0),
            checkEvolution: checkEvolution
        );

    [Fact]
    public async Task Runner_OnLevelUp_OffersAndAppliesEvolution_WhenAllowed()
    {
        var player = Fighter("CHARMANDER", hp: 200, attack: 999, speed: 100, level: 5);
        player.SpeciesId = 4;
        var (_, outcome) = Charmeleon();

        int checks = 0;
        var recorder = new RecordingEmitter();
        // Default ScriptedInput allows the evolution (ConfirmEvolutionAsync default = true).
        var runner = BuildRunner(
            player,
            new ScriptedInput("tackle"),
            recorder,
            _ => Task.FromResult<EvolutionOutcome?>(checks++ == 0 ? outcome : null)
        );

        await runner.RunAsync();

        // Offered, then evolved — both carry the from/to identity for the modal + morph.
        var offered = Assert.Single(recorder.Of<EvolutionOffered>());
        Assert.Equal(
            (4, 5, "CHARMANDER", "CHARMELEON"),
            (offered.FromSpeciesId, offered.ToSpeciesId, offered.FromName, offered.ToName)
        );
        var evolved = Assert.Single(recorder.Of<CreatureEvolved>());
        Assert.Equal(5, evolved.ToSpeciesId);

        // The player was mutated into the new form and the evolved learnset was seated.
        Assert.Equal(5, player.SpeciesId);
        Assert.Equal("CHARMELEON", player.Name);
        Assert.Equal(58, player.BaseHP);

        // The evolved form's level-6 move was learned, after the evolution event.
        var learned = Assert.Single(recorder.Of<MoveLearned>());
        Assert.Equal("flamethrower", learned.MoveName);
        Assert.Contains(player.MoveSet, m => m.Base.Id == 53);
        Assert.Empty(recorder.Of<EvolutionCancelled>());

        var events = recorder.Events.ToList();
        Assert.True(events.IndexOf(offered) < events.IndexOf(evolved));
        Assert.True(events.IndexOf(evolved) < events.IndexOf(learned));
    }

    [Fact]
    public async Task Runner_OnLevelUp_Cancelled_LeavesCreatureUnchanged()
    {
        var player = Fighter("CHARMANDER", hp: 200, attack: 999, speed: 100, level: 5);
        player.SpeciesId = 4;
        var (_, outcome) = Charmeleon();

        var recorder = new RecordingEmitter();
        // This input declines the evolution (Gen 1 B-cancel). It re-offers at the next level-up (not tested
        // here — the run ends after this battle); the point is the creature is untouched on cancel.
        var runner = BuildRunner(
            player,
            new CancelEvolutionInput("tackle"),
            recorder,
            _ => Task.FromResult<EvolutionOutcome?>(outcome)
        );

        await runner.RunAsync();

        Assert.Single(recorder.Of<EvolutionOffered>());
        Assert.Single(recorder.Of<EvolutionCancelled>());
        Assert.Empty(recorder.Of<CreatureEvolved>());

        // Untouched: still Charmander, no evolution move learned.
        Assert.Equal(4, player.SpeciesId);
        Assert.Equal("CHARMANDER", player.Name);
        Assert.DoesNotContain(player.MoveSet, m => m.Base.Id == 53);
    }

    [Fact]
    public async Task Runner_NoLevelUp_DoesNotOfferEvolution()
    {
        // A level-50 player winning a tiny-XP pushover gains no level → the evolution check never runs, even
        // though the resolver would return an outcome.
        var player = Fighter("CHARMANDER", hp: 200, attack: 999, speed: 100, level: 50);
        player.SpeciesId = 4;
        var (_, outcome) = Charmeleon();

        int calls = 0;
        var recorder = new RecordingEmitter();
        var runner = BuildRunner(
            player,
            new ScriptedInput("tackle"),
            recorder,
            _ =>
            {
                calls++;
                return Task.FromResult<EvolutionOutcome?>(outcome);
            }
        );

        await runner.RunAsync();

        Assert.Equal(0, calls); // resolver never consulted without a level-up
        Assert.Empty(recorder.Of<EvolutionOffered>());
        Assert.Empty(recorder.Of<CreatureEvolved>());
    }

    [Fact]
    public async Task Runner_NullResolver_BehavesAsPlainChain_NoEvolution()
    {
        var player = Fighter("PLAYER", hp: 200, attack: 999, speed: 100, level: 5);

        var recorder = new RecordingEmitter();
        var runner = new BattleRunner(
            player,
            WinThenLose(),
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0)
        );

        await runner.RunAsync();

        Assert.Empty(recorder.Of<EvolutionOffered>());
        Assert.Empty(recorder.Of<CreatureEvolved>());
        Assert.Single(recorder.Of<RunEnded>());
    }

    // A player input that plays a scripted move but declines any evolution offer.
    private sealed class CancelEvolutionInput(string move) : IBattleInput
    {
        private readonly ScriptedInput _inner = new(move);

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            _inner.ChooseMoveAsync(context);

        public Task<bool> ConfirmEvolutionAsync(EvolutionPromptContext context) =>
            Task.FromResult(false);
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
