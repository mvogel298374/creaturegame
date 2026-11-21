using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
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

    private static Attack MetronomeMove() =>
        new()
        {
            Id = 118,
            Name = "metronome",
            Accuracy = 100,
            PowerPointsMax = 10,
            Effect = MoveEffect.Metronome,
        };

    private static Attack TackleMove() =>
        new()
        {
            Id = 33,
            Name = "tackle",
            BaseDamage = 40,
            Accuracy = 100,
            PowerPointsMax = 35,
            AttackType = AttackType.Physical,
            DamageType = DamageType.Normal,
        };

    [Fact]
    public async Task MetronomeCanCallMirrorMoveWhichFailsBeforeTheFoeMovesThenCopiesItsLastMove()
    {
        var player = TestCreatures.Make("Player", hp: 2000, attack: 200, speed: 200);
        player.AddAttack(MetronomeMove());
        var enemy = TestCreatures.Make("Enemy", hp: 150, defense: 20, speed: 1);
        enemy.AddAttack(TackleMove());
        var mirrorMove = new Attack
        {
            Id = 119,
            Name = "mirror-move",
            Accuracy = 100,
            PowerPointsMax = 20,
            Effect = MoveEffect.MirrorMove,
        };

        var emitter = await FullBattle.Fight(
            player,
            enemy,
            new ScriptedInput("metronome"),
            new ScriptedInput("tackle"),
            movePool: [mirrorMove]
        );

        var events = emitter.Events.ToList();
        int failed = events.FindIndex(e =>
            e is MoveMissed { AttackerName: "Player", MoveName: "mirror-move" }
        );
        int copied = events.FindIndex(e =>
            e is MoveUsed { AttackerName: "Player", MoveName: "tackle" }
        );
        Assert.True(failed >= 0, "turn 1: the called Mirror Move fails, the foe has not moved yet");
        Assert.True(copied > failed, "turn 2: the called Mirror Move copies the foe's Tackle");
        Assert.Equal(
            ["metronome", "mirror-move", "metronome", "mirror-move", "tackle"],
            emitter
                .Of<MoveUsed>()
                .Where(e => e.AttackerName == "Player")
                .Select(e => e.MoveName)
                .Take(5)
        );
        Assert.False(enemy.IsAlive());
    }

    [Fact]
    public async Task MetronomeNeverCallsStruggle()
    {
        var attacker = TestCreatures.Make("Attacker");
        var defender = TestCreatures.Make("Defender");
        int fullHp = defender.Attributes.HP;
        attacker.AddAttack(MetronomeMove());
        var struggle = new Attack
        {
            Id = 165,
            Name = "struggle",
            BaseDamage = 50,
            Accuracy = 100,
        };
        var recorder = new RecordingEmitter();

        await new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            rules: AlwaysHitRules.Instance,
            emitter: recorder,
            movePool: [struggle]
        ).ExecuteAsync();

        Assert.Equal(fullHp, defender.Attributes.HP);
        Assert.Equal("metronome", Assert.Single(recorder.Of<MoveUsed>()).MoveName);
    }

    [Fact]
    public async Task TheSeamDecidesWhatMetronomeCanCall()
    {
        var attacker = TestCreatures.Make("Attacker");
        var defender = TestCreatures.Make("Defender");
        int fullHp = defender.Attributes.HP;
        attacker.AddAttack(MetronomeMove());
        var recorder = new RecordingEmitter();

        await new AttackAction(
            attacker,
            defender,
            attacker.MoveSet[0],
            new Gen1TypeChart(),
            rules: new CallsNothingRules(),
            emitter: recorder,
            movePool: [TackleMove()]
        ).ExecuteAsync();

        Assert.Equal(fullHp, defender.Attributes.HP);
        Assert.Equal("metronome", Assert.Single(recorder.Of<MoveUsed>()).MoveName);
    }

    private sealed class CallsNothingRules : DelegatingBattleRules
    {
        public override bool MetronomeCanCall(Attack move) => false;
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
