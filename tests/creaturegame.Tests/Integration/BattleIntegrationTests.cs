using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Tests Battle.StartFightAsync() as an integrated unit: turn loop, faint detection,
/// the mid-turn dead-target guard, the out-of-PP → Struggle path, and the
/// player-controlled input path (mirrors SignalRInput behaviour without the web layer).
/// </summary>
public class BattleIntegrationTests
{
    [Fact]
    public async Task Battle_EndsWhenEnemyFaints()
    {
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Name = "Slam",
                BaseDamage = 100,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            AutoSelectInput.Instance,
            AutoSelectInput.Instance
        );
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.IsAlive());
    }

    [Fact]
    public async Task Battle_EndsWhenPlayerFaints()
    {
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.HP = 1;
        player.Attributes.MaxHP = 1;
        player.Attributes.Speed = 1;
        player.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 40,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.Attack = 999;
        enemy.Attributes.Speed = 100;
        enemy.AddAttack(
            new Attack
            {
                Name = "Slam",
                BaseDamage = 100,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            AutoSelectInput.Instance,
            AutoSelectInput.Instance
        );
        await battle.StartFightAsync();

        Assert.False(player.IsAlive());
        Assert.True(enemy.IsAlive());
    }

    [Fact]
    public async Task Battle_SlowerCreature_ActionSkipped_WhenKilledFirst()
    {
        // Player goes first (speed 200 > 1) and kills enemy in one hit.
        // Enemy.Source.IsAlive() == false when its turn comes → action is skipped.
        // Enemy has attack 999: if the guard were absent it would devastate the player.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 200;
        player.AddAttack(
            new Attack
            {
                Name = "Slam",
                BaseDamage = 100,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Attack = 999;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Slam",
                BaseDamage = 100,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        int playerHpBefore = player.Attributes.HP;
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            AutoSelectInput.Instance,
            AutoSelectInput.Instance
        );
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.Equal(playerHpBefore, player.Attributes.HP);
    }

    [Fact]
    public async Task Battle_UsesStruggle_WhenPPExhausted()
    {
        // Player has a 0-damage move with 1 PP — does no damage so enemy survives turn 1.
        // Enemy has 1 HP and a 0-damage move so player survives too.
        // Turn 2: Battle sees no selectable move (out of PP), passes null → AttackAction uses Struggle →
        // enemy (1 HP) faints and player takes recoil.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.HP = 500;
        player.Attributes.MaxHP = 500;
        player.Attributes.Attack = 50;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Name = "Splash",
                BaseDamage = 0,
                Accuracy = 100,
                AttackType = AttackType.Physical,
                PowerPointsMax = 1,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Splash",
                BaseDamage = 0,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        // Battle skips Console.ReadKey() when Console.IsInputRedirected (test context).
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            AutoSelectInput.Instance,
            AutoSelectInput.Instance
        );
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.Attributes.HP < player.Attributes.MaxHP); // recoil damage landed
    }

    // ── Player-controlled input tests (TurnControlledInput mirrors SignalRInput) ──

    [Fact]
    public async Task Battle_WithPlayerInput_PicksSpecificMoveByIndex_ThatMoveIsExecuted()
    {
        // Move 0 = 0-damage Splash (does nothing); Move 1 = Slam (kills 1-HP enemy).
        // TurnControlledInput picks index 1 → Slam must be the move used.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Id = 1,
                Name = "Splash",
                BaseDamage = 0,
                Accuracy = 100,
                PowerPointsMax = 5,
                AttackType = AttackType.Physical,
            }
        );
        player.AddAttack(
            new Attack
            {
                Id = 2,
                Name = "Slam",
                BaseDamage = 100,
                Accuracy = 100,
                PowerPointsMax = 5,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 10,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        var input = new TurnControlledInput(1); // pick Slam every turn
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            input,
            AutoSelectInput.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        var moveUsed = emitter.Events.OfType<MoveUsed>().First(e => e.AttackerName == "Player");
        Assert.Equal("Slam", moveUsed.MoveName);
    }

    [Fact]
    public async Task Battle_WithPlayerInput_FallsBackToFirstAvailable_WhenChosenIndexHasNoPP()
    {
        // Move 0 has PP forced to 0; Move 1 = Slam (damage move).
        // TurnControlledInput sends index 0 → no PP → fallback → Slam used.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Id = 1,
                Name = "Splash",
                BaseDamage = 0,
                Accuracy = 100,
                PowerPointsMax = 1,
                AttackType = AttackType.Physical,
            }
        );
        player.AddAttack(
            new Attack
            {
                Id = 2,
                Name = "Slam",
                BaseDamage = 100,
                Accuracy = 100,
                PowerPointsMax = 5,
                AttackType = AttackType.Physical,
            }
        );
        player.MoveSet[0].PowerPointsCurrent = 0; // exhaust Splash manually

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 10,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        var input = new TurnControlledInput(0); // asks for Splash (0 PP) → must fall back
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            input,
            AutoSelectInput.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        var moveUsed = emitter.Events.OfType<MoveUsed>().First(e => e.AttackerName == "Player");
        Assert.Equal("Slam", moveUsed.MoveName);
    }

    [Fact]
    public async Task Battle_TurnStartedEvent_ContainsCorrectPlayerEnemyDataAndMoveList()
    {
        // Run 1 turn: enemy has 1 HP, dies on first hit.
        // Verify TurnStarted carries player's real MaxHP, enemy's real MaxHP, and the move list.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Id = 1,
                Name = "Tackle",
                BaseDamage = 40,
                Accuracy = 100,
                PowerPointsMax = 35,
                AttackType = AttackType.Physical,
            }
        );
        player.AddAttack(
            new Attack
            {
                Id = 2,
                Name = "Vine Whip",
                BaseDamage = 35,
                Accuracy = 100,
                PowerPointsMax = 25,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 200;
        enemy.Attributes.Speed = 1;
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
        var input = new TurnControlledInput(0);
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            input,
            AutoSelectInput.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        var ts = emitter.Events.OfType<TurnStarted>().First();
        Assert.Equal("Player", ts.PlayerName);
        Assert.Equal("Enemy", ts.EnemyName);
        Assert.Equal(player.Attributes.MaxHP, ts.PlayerMaxHp);
        Assert.Equal(200, ts.EnemyMaxHp);
        Assert.Equal(2, ts.PlayerMoves.Count);
        Assert.Contains(ts.PlayerMoves, m => m.Name == "Tackle");
        Assert.Contains(ts.PlayerMoves, m => m.Name == "Vine Whip");
        Assert.All(ts.PlayerMoves, m => Assert.True(m.PpCurrent > 0));
    }

    [Fact]
    public async Task Battle_FullSimulation_MultiTurn_BattleStartedAndEndedEventsFire()
    {
        // Both sides survive multiple turns — verifies the full turn loop runs correctly
        // under player-controlled input (TurnControlledInput always picking move 0).
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.HP = 100;
        player.Attributes.MaxHP = 100;
        player.Attributes.Attack = 20;
        player.Attributes.Defense = 50;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 40,
                Accuracy = 100,
                PowerPointsMax = 35,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP = 100;
        enemy.Attributes.MaxHP = 100;
        enemy.Attributes.Attack = 15;
        enemy.Attributes.Defense = 50;
        enemy.Attributes.Speed = 50;
        enemy.AddAttack(
            new Attack
            {
                Name = "Scratch",
                BaseDamage = 40,
                Accuracy = 100,
                PowerPointsMax = 35,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        // 100 choices of 0 is far more than the expected ~10–15 turns needed for one side to faint
        var input = new TurnControlledInput(Enumerable.Repeat(0, 100).ToArray());
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            input,
            AutoSelectInput.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        Assert.Contains(emitter.Events, e => e is BattleStarted);
        Assert.True(emitter.Events.OfType<TurnStarted>().Count() >= 2, "Expected multiple turns");
        var ended = emitter.Events.OfType<BattleEnded>().SingleOrDefault();
        Assert.NotNull(ended);
        Assert.True(ended.WinnerName == "Player" || ended.WinnerName == "Enemy");
        Assert.True(!player.IsAlive() || !enemy.IsAlive(), "One side must have fainted");
    }

    [Fact]
    public async Task Battle_FullSimulation_EventsAreOrderedCorrectly()
    {
        // BattleStarted → (TurnStarted → ... → TurnEnded)* → CreatureFainted → BattleEnded
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Attack = 999;
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Name = "Slam",
                BaseDamage = 100,
                Accuracy = 100,
                PowerPointsMax = 35,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 1;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Tackle",
                BaseDamage = 10,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        var input = new TurnControlledInput(0);
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            input,
            AutoSelectInput.Instance,
            emitter: emitter
        );
        await battle.StartFightAsync();

        var events = emitter.Events;
        Assert.IsType<BattleStarted>(events[0]);
        Assert.IsType<TurnStarted>(events[1]);
        Assert.IsType<BattleEnded>(events[^1]);
        Assert.Contains(events, e => e is CreatureFainted);
        // BattleEnded must come after CreatureFainted
        int faintedIdx = events.ToList().FindIndex(e => e is CreatureFainted);
        int endedIdx = events.ToList().FindIndex(e => e is BattleEnded);
        Assert.True(endedIdx > faintedIdx);
    }

    [Fact]
    public async Task Battle_PoisonedEnemy_FaintsByEndOfTurnDamage()
    {
        // Enemy has exactly 1 HP and is Poisoned — end-of-turn damage (min 1) finishes it.
        // Both creatures use 0-damage moves so no direct attack can kill either side.
        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.Speed = 100;
        player.AddAttack(
            new Attack
            {
                Name = "Splash",
                BaseDamage = 0,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50, Status = StatusCondition.Poison };
        enemy.CalculateStats();
        enemy.Attributes.HP = 1;
        enemy.Attributes.MaxHP = 160;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(
            new Attack
            {
                Name = "Splash",
                BaseDamage = 0,
                Accuracy = 100,
                AttackType = AttackType.Physical,
            }
        );

        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            AutoSelectInput.Instance,
            AutoSelectInput.Instance
        );
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive());
        Assert.True(player.IsAlive());
    }

    [Fact]
    public async Task Battle_SameSeed_ProducesIdenticalEventSequence()
    {
        // The IRandomSource seam makes a seeded battle fully reproducible: two runs wired
        // with the same seed must emit byte-for-byte identical event streams. If any roll
        // still reached the global Random.Shared, the two streams would diverge and fail.
        string first = await RunSeededBattle(1234);
        string second = await RunSeededBattle(1234);

        Assert.Equal(first, second);
        Assert.Contains(nameof(BattleEnded), first);
        Assert.True(
            first.Split('|').Count(s => s.Contains(nameof(TurnStarted))) >= 3,
            "Battle should run several turns so the RNG is actually exercised"
        );
    }

    /// <summary>
    /// Runs a complete battle on a seeded RNG and returns the ordered event stream as a
    /// signature string. Fresh creatures each call; the same seed wired into both the rules
    /// and the battle drives every roll (crit, accuracy, damage variance, tie-break).
    /// </summary>
    private static async Task<string> RunSeededBattle(int seed)
    {
        var rng = new SeededRandomSource(seed);
        var rules = new Gen1BattleRules(rng);

        var player = new Creature("Player") { Level = 50 };
        player.CalculateStats();
        player.Attributes.MaxHP = 150;
        player.Attributes.HP = 150;
        player.Attributes.Attack = 80;
        player.Attributes.Defense = 70;
        player.Attributes.Speed = 80;
        player.AddAttack(
            new Attack
            {
                Name = "Body Slam",
                BaseDamage = 85,
                Accuracy = 85,
                PowerPointsMax = 35,
                AttackType = AttackType.Physical,
            }
        );

        var enemy = new Creature("Enemy") { Level = 50 };
        enemy.CalculateStats();
        enemy.Attributes.MaxHP = 150;
        enemy.Attributes.HP = 150;
        enemy.Attributes.Attack = 78;
        enemy.Attributes.Defense = 72;
        enemy.Attributes.Speed = 75;
        enemy.AddAttack(
            new Attack
            {
                Name = "Stomp",
                BaseDamage = 80,
                Accuracy = 85,
                PowerPointsMax = 35,
                AttackType = AttackType.Physical,
            }
        );

        var emitter = new RecordingEmitter();
        var input = new TurnControlledInput(Enumerable.Repeat(0, 200).ToArray());
        var battle = new Battle(
            player,
            enemy,
            new Gen1TypeChart(),
            input,
            AutoSelectInput.Instance,
            rules: rules,
            emitter: emitter,
            rng: rng
        );
        await battle.StartFightAsync();

        return string.Join("|", emitter.Events.Select(e => e.ToString()));
    }

    // ── Test helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Deterministic IBattleInput: dequeues pre-supplied move indices, defaulting to 0
    /// when the queue is empty. Falls back to the first available move when the chosen
    /// index has 0 PP — mirrors the SignalRInput fallback without the web layer.
    /// </summary>
    private sealed class TurnControlledInput(params int[] choices) : IBattleInput
    {
        private readonly Queue<int> _choices = new(choices);

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context)
        {
            var moves = context.Attacker.MoveSet;
            int index = _choices.Count > 0 ? _choices.Dequeue() : 0;

            if (index >= 0 && index < moves.Count && moves[index].PowerPointsCurrent > 0)
                return Task.FromResult(moves[index]);

            return Task.FromResult(moves.First(m => m.PowerPointsCurrent > 0));
        }
    }
}
