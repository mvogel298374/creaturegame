using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.DB;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

public class MetronomeTests
{
    [Fact]
    public async Task Metronome_CallsMovesFromPool_EmitsBothMoveUsedEvents()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var metronome = new Attack
        {
            Id = 1,
            Name = "metronome",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.Metronome,
        };
        var tackle = new Attack
        {
            Id = 2,
            Name = "Tackle",
            BaseDamage = 40,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Normal,
        };
        attacker.AddAttack(metronome);

        // Pool has only one eligible move (Tackle) after excluding Metronome — deterministic
        var pool = new List<Attack> { metronome, tackle };
        var recorder = new RecordingEmitter();

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            rules: AlwaysHitRules.Instance,
            emitter: recorder,
            movePool: pool
        );
        await action.ExecuteAsync();

        var moveUsedEvents = recorder.Of<MoveUsed>().ToList();
        Assert.Equal(2, moveUsedEvents.Count);
        Assert.Equal("metronome", moveUsedEvents[0].MoveName);
        Assert.Equal("Tackle", moveUsedEvents[1].MoveName);
    }

    [Fact]
    public async Task Metronome_CalledMove_DealsDamage()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        int fullHp = defender.Attributes.HP;

        var metronome = new Attack
        {
            Id = 1,
            Name = "metronome",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.Metronome,
        };
        var tackle = new Attack
        {
            Id = 2,
            Name = "Tackle",
            BaseDamage = 40,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Normal,
        };
        attacker.AddAttack(metronome);

        var pool = new List<Attack> { metronome, tackle };
        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            rules: AlwaysHitRules.Instance,
            emitter: ConsoleBattleEventEmitter.Instance,
            movePool: pool
        );
        await action.ExecuteAsync();

        Assert.True(
            defender.Attributes.HP < fullHp,
            "Tackle called by Metronome should deal damage"
        );
    }

    [Fact]
    public async Task Metronome_NeverCallsMetronomeItself()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var metronome = new Attack
        {
            Id = 1,
            Name = "metronome",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.Metronome,
        };
        var tackle = new Attack
        {
            Id = 2,
            Name = "Tackle",
            BaseDamage = 1,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Normal,
        };
        attacker.AddAttack(metronome);

        var pool = new List<Attack> { metronome, tackle };

        // Run 50 times — with only one eligible move the result is deterministic,
        // but 50 iterations confirms no recursion guard regression.
        for (int i = 0; i < 50; i++)
        {
            defender.Attributes.HP = defender.Attributes.MaxHP;
            var recorder = new RecordingEmitter();
            var action = new AttackAction(
                attacker,
                defender,
                attacker.MoveSet[0],
                new Gen1TypeChart(),
                rules: AlwaysHitRules.Instance,
                emitter: recorder,
                movePool: pool
            );
            await action.ExecuteAsync();

            var moves = recorder.Of<MoveUsed>().Select(e => e.MoveName).ToList();
            Assert.DoesNotContain("metronome", moves.Skip(1), StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Metronome_NeverCallsMirrorMove()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var metronome = new Attack
        {
            Id = 1,
            Name = "metronome",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.Metronome,
        };
        var mirrorMove = new Attack
        {
            Id = 2,
            Name = "mirror-move",
            BaseDamage = 0,
            Accuracy = 100,
        };
        var tackle = new Attack
        {
            Id = 3,
            Name = "Tackle",
            BaseDamage = 1,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Normal,
        };
        attacker.AddAttack(metronome);

        var pool = new List<Attack> { metronome, mirrorMove, tackle };

        for (int i = 0; i < 50; i++)
        {
            defender.Attributes.HP = defender.Attributes.MaxHP;
            var recorder = new RecordingEmitter();
            var action = new AttackAction(
                attacker,
                defender,
                attacker.MoveSet[0],
                new Gen1TypeChart(),
                rules: AlwaysHitRules.Instance,
                emitter: recorder,
                movePool: pool
            );
            await action.ExecuteAsync();

            var calledMoves = recorder.Of<MoveUsed>().Skip(1).Select(e => e.MoveName).ToList();
            Assert.DoesNotContain("mirror-move", calledMoves, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Metronome_WithNoEligibleMoves_DoesNothing()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();
        int fullHp = defender.Attributes.HP;

        var metronome = new Attack
        {
            Id = 1,
            Name = "metronome",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.Metronome,
        };
        var mirrorMove = new Attack
        {
            Id = 2,
            Name = "mirror-move",
            BaseDamage = 0,
            Accuracy = 100,
        };
        attacker.AddAttack(metronome);

        // Pool contains only excluded moves → eligible list is empty → no inner action
        var pool = new List<Attack> { metronome, mirrorMove };
        var recorder = new RecordingEmitter();

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            rules: AlwaysHitRules.Instance,
            emitter: recorder,
            movePool: pool
        );
        await action.ExecuteAsync();

        Assert.Equal(fullHp, defender.Attributes.HP);
        Assert.Single(recorder.Of<MoveUsed>()); // only "metronome" itself, no inner move
    }

    [Fact]
    public async Task Metronome_CalledMove_ExecutesFullPipeline_IncludingStatus()
    {
        var attacker = new Creature("Attacker") { Level = 50 };
        attacker.CalculateStats();
        var defender = new Creature("Defender") { Level = 50 };
        defender.CalculateStats();

        var metronome = new Attack
        {
            Id = 1,
            Name = "metronome",
            BaseDamage = 0,
            Accuracy = 100,
            Effect = MoveEffect.Metronome,
        };
        var sleepPowder = new Attack
        {
            Id = 2,
            Name = "sleep-powder",
            BaseDamage = 0,
            Accuracy = 100,
            StatusEffect = StatusCondition.Sleep,
            EffectChance = 100,
        };
        attacker.AddAttack(metronome);

        var pool = new List<Attack> { metronome, sleepPowder };
        var recorder = new RecordingEmitter();

        var action = new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            rules: AlwaysHitRules.Instance,
            emitter: recorder,
            movePool: pool
        );
        await action.ExecuteAsync();

        Assert.Equal(StatusCondition.Sleep, defender.Battle.Status);
        Assert.NotEmpty(recorder.Of<StatusApplied>());
    }
}
