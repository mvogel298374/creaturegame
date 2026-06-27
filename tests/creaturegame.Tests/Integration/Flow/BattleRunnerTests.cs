using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>
/// The endless-battle-chain orchestrator: one persistent player runs encounter after encounter (enemies
/// supplied by a delegate so no DB is involved here) until it faints, then a single RunEnded carries the
/// summary. Verifies the win-counting, the per-encounter BattleStarted stream, cross-encounter XP
/// accumulation, and that RunEnded fires exactly once with the right totals.
/// </summary>
public class BattleRunnerTests
{
    [Fact]
    public async Task Runner_ChainsWins_ThenEmitsRunEndedWithSummary_WhenPlayerFaints()
    {
        const int targetWins = 3;
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
        int startXp = player.Experience;

        // The first `targetWins` enemies are slow 1-HP pushovers (player strikes first, one-shots, takes no
        // damage); the next is a fast bruiser that one-shots the player and ends the run.
        int built = 0;
        Func<Creature, int, Task<Creature>> supplier = (_, _) =>
        {
            built++;
            var enemy =
                built <= targetWins
                    ? Fighter($"Pushover{built}", hp: 1, attack: 5, speed: 1, level: 5)
                    : Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            enemy.SpeciesBaseExperience = 50; // small award at L50 — XP grows, no level-up
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

        // Exactly one RunEnded, after the player has fainted, with the correct summary.
        var runEnded = Assert.Single(recorder.Of<RunEnded>());
        Assert.Equal(targetWins, runEnded.BattlesWon);
        Assert.Equal("Player", runEnded.FinalCreatureName);
        Assert.Equal(player.Level, runEnded.FinalLevel);
        Assert.False(player.IsAlive());

        // One encounter started per win plus the fatal one.
        Assert.Equal(targetWins + 1, recorder.Of<BattleStarted>().Count());
        Assert.Equal(targetWins + 1, built);

        // XP accumulated across the wins (no level-up at L50, so just the running total).
        Assert.True(player.Experience > startXp, "XP accumulates across chained encounters");
    }

    // Load-bearing for the client: RunEnded drives the game-over screen, so it must fire ONLY on a real
    // faint — never when the run is abandoned (client disconnect cancels the input mid-fight). A cancelled
    // input throws out of RunAsync before the emit, so no RunEnded should be recorded.
    [Fact]
    public async Task Runner_DoesNotEmitRunEnded_WhenInputIsCancelledMidRun()
    {
        var player = Fighter("Player", hp: 200, attack: 999, speed: 100, level: 50);
        var enemy = Fighter("Enemy", hp: 999, attack: 1, speed: 1, level: 50); // survives, so a turn is needed

        var recorder = new RecordingEmitter();

        var runner = new BattleRunner(
            player,
            (_, _) => Task.FromResult(enemy),
            Gen1TypeChart.Instance,
            new CancelledInput(), // simulates the client having dropped — input throws on first choice
            new ScriptedInput("tackle"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(),
            rng: new SeededRandomSource(0)
        );

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync());
        Assert.Empty(recorder.Of<RunEnded>());
    }

    // Gen 1 keeps major status on a Pokémon out of battle, so it must carry into the next encounter — and
    // Toxic reverts to regular Poison out of battle. Encounter 1 badly-poisons the player; encounter 2 must
    // start with the player already (regularly) Poisoned, never BadPoisoned.
    [Fact]
    public async Task Runner_CarriesMajorStatusAcrossEncounters_NormalisingToxicToPoison()
    {
        var player = Fighter("Player", hp: 300, attack: 999, speed: 1, level: 50); // slow: the foe strikes first

        int built = 0;
        Func<Creature, int, Task<Creature>> supplier = (_, _) =>
        {
            built++;
            Creature enemy;
            if (built == 1)
            {
                // A 1-HP foe that badly-poisons the player (forced) before being one-shot.
                enemy = Fighter("Toxic", hp: 1, attack: 5, speed: 999, level: 50);
                enemy.MoveSet.Clear();
                enemy.AddAttack(
                    new Attack
                    {
                        Name = "toxic",
                        BaseDamage = 5,
                        Accuracy = 100,
                        AttackType = AttackType.Physical,
                        PowerPointsMax = 99,
                        StatusEffect = StatusCondition.BadPoison,
                        EffectChance = 100,
                    }
                );
            }
            else
            {
                enemy = Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50); // ends the run
            }
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new BattleRunner(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new RandomMoveInput(), // enemy uses whatever move it has (toxic, then tackle)
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic().ForceSecondary(),
            rng: new SeededRandomSource(0)
        );

        await runner.RunAsync();

        // Proves the carry actually fired (not vacuously): the player ends the run regularly Poisoned —
        // that status was wiped by encounter 2's reset and only re-appears because it was carried + normalised.
        Assert.Equal(StatusCondition.Poison, player.Battle.Status);

        var playerStatuses = recorder.Of<TurnStarted>().Select(t => t.PlayerStatus).ToList();
        // Encounter 2's first turn sees the carried status; it must be regular Poison, never BadPoison.
        Assert.Contains(StatusCondition.Poison, playerStatuses);
        Assert.DoesNotContain(StatusCondition.BadPoison, playerStatuses);
    }

    // Roguelite Poké Center: after every 3rd win the player is fully restored (HP, PP, status) before the
    // next encounter. Encounters 1–3 are fast foes that chip + poison the slow player; the heal must fire
    // exactly once (after win 3) and the player must enter encounter 4 at full HP, full PP, and unstatused.
    [Fact]
    public async Task Runner_FullyHealsPlayer_AfterEveryThirdWin_RestoringHpPpAndClearingStatus()
    {
        var player = Fighter("Player", hp: 250, attack: 999, speed: 1, level: 50); // slow: foes strike first
        int maxHp = player.Attributes.MaxHP;
        int maxPp = player.MoveSet[0].Base.PowerPointsMax;

        // Capture the player's condition at the moment encounter 4 is built — i.e. just after the win-3 heal.
        int hpEntering4 = -1,
            ppEntering4 = -1;
        var statusEntering4 = StatusCondition.Sleep; // sentinel; overwritten when encounter 4 is built

        int built = 0;
        Func<Creature, int, Task<Creature>> supplier = (p, _) =>
        {
            built++;
            if (built == 4)
            {
                hpEntering4 = p.Attributes.HP;
                ppEntering4 = p.MoveSet[0].PowerPointsCurrent;
                statusEntering4 = p.Battle.Status;
            }
            Creature enemy;
            if (built <= 3)
            {
                // A 1-HP foe that out-speeds the player: it lands a chip hit + poison, then is one-shot.
                enemy = Fighter("Chip", hp: 1, attack: 20, speed: 999, level: 50);
                enemy.MoveSet.Clear();
                enemy.AddAttack(
                    new Attack
                    {
                        Name = "poisonbite",
                        BaseDamage = 20,
                        Accuracy = 100,
                        AttackType = AttackType.Physical,
                        PowerPointsMax = 99,
                        StatusEffect = StatusCondition.Poison,
                        EffectChance = 100,
                    }
                );
            }
            else
            {
                enemy = Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50); // ends the run
            }
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new BattleRunner(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle"),
            new RandomMoveInput(),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic().ForceSecondary(),
            rng: new SeededRandomSource(0)
        );

        await runner.RunAsync();

        // The offer + heal each fired exactly once — after win 3, not after wins 1 or 2.
        Assert.Single(recorder.Of<RecoveryOffered>());
        var recovered = Assert.Single(recorder.Of<PlayerRecovered>());
        Assert.Equal("Player", recovered.CreatureName);
        Assert.Equal(maxHp, recovered.HpAfter);
        Assert.Empty(recorder.Of<RecoveryDeclined>()); // the default input accepts

        // Entering encounter 4 (right after that heal) everything is restored.
        Assert.Equal(maxHp, hpEntering4); // HP back to full despite three chip hits + poison ticks
        Assert.Equal(maxPp, ppEntering4); // PP restored despite three tackles
        Assert.Equal(StatusCondition.None, statusEntering4); // poison cured by the Center

        // Not vacuous: the player really was poisoned across the first three encounters.
        Assert.Contains(
            StatusCondition.Poison,
            recorder.Of<TurnStarted>().Select(t => t.PlayerStatus)
        );
    }

    // The recovery offer is a real choice: a player who declines is NOT healed. After win 3 the declining
    // input skips the heal, so the player enters encounter 4 still wounded + poisoned, RecoveryDeclined fires,
    // and no PlayerRecovered is emitted.
    [Fact]
    public async Task Runner_DoesNotHeal_WhenPlayerDeclinesRecovery()
    {
        var player = Fighter("Player", hp: 250, attack: 999, speed: 1, level: 50); // slow: foes strike first
        int maxHp = player.Attributes.MaxHP;

        int built = 0;
        int hpEntering4 = -1;
        var statusEntering4 = StatusCondition.None;
        Func<Creature, int, Task<Creature>> supplier = (p, _) =>
        {
            built++;
            if (built == 4)
            {
                hpEntering4 = p.Attributes.HP;
                statusEntering4 = p.Battle.Status;
            }
            Creature enemy;
            if (built <= 3)
            {
                enemy = Fighter("Chip", hp: 1, attack: 20, speed: 999, level: 50);
                enemy.MoveSet.Clear();
                enemy.AddAttack(
                    new Attack
                    {
                        Name = "poisonbite",
                        BaseDamage = 20,
                        Accuracy = 100,
                        AttackType = AttackType.Physical,
                        PowerPointsMax = 99,
                        StatusEffect = StatusCondition.Poison,
                        EffectChance = 100,
                    }
                );
            }
            else
            {
                enemy = Fighter("Bruiser", hp: 999, attack: 999, speed: 999, level: 50);
            }
            enemy.SpeciesBaseExperience = 50;
            return Task.FromResult(enemy);
        };

        var recorder = new RecordingEmitter();
        var runner = new BattleRunner(
            player,
            supplier,
            Gen1TypeChart.Instance,
            new ScriptedInput("tackle").DeclinesRecovery(),
            new RandomMoveInput(),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic().ForceSecondary(),
            rng: new SeededRandomSource(0)
        );

        await runner.RunAsync();

        // The offer was made, but declined — no heal.
        Assert.Single(recorder.Of<RecoveryOffered>());
        Assert.Single(recorder.Of<RecoveryDeclined>());
        Assert.Empty(recorder.Of<PlayerRecovered>());

        // Entering encounter 4 the player is still wounded and still poisoned (status carried, not cured).
        Assert.True(hpEntering4 > 0 && hpEntering4 < maxHp, "declining leaves the player wounded");
        Assert.Equal(StatusCondition.Poison, statusEntering4);
    }

    // A mutual end-of-turn DoT faint: both creatures survive the (0-damage) poison-move turn but are now
    // poisoned, then BOTH drop on the same end-of-turn poison tick. The enemy-faint branch is checked first
    // (so XP is awarded), but the player is dead too — so the run ends as a LOSS and the would-be win is
    // never counted. Pins the documented double-faint edge (TODO Known Gaps): it counts as a loss, full stop.
    [Fact]
    public async Task Runner_DoubleFaintFromEndOfTurnPoison_CountsAsLoss_NotAWin()
    {
        // maxHP 160 → poison tick = 160/16 = 10; HP 5 → the first tick is lethal. Both use a 0-damage poison
        // move, so the attack phase changes nothing and both are alive (poisoned) entering end-of-turn.
        var player = Poisoner("Player", maxHp: 160, hp: 5, speed: 100);
        var enemy = Poisoner("Enemy", maxHp: 160, hp: 5, speed: 1);

        var recorder = new RecordingEmitter();
        var runner = new BattleRunner(
            player,
            (_, _) => Task.FromResult(enemy),
            Gen1TypeChart.Instance,
            new ScriptedInput("poisonpowder"),
            new ScriptedInput("poisonpowder"),
            movePool: Array.Empty<Attack>(),
            emitter: recorder,
            rules: new ScriptableRules().Deterministic(), // alwaysHit so each status move lands
            rng: new SeededRandomSource(0)
        );

        await runner.RunAsync();

        // Both fainted, and both from a poison tick on the same end-of-turn (two StatusDamage/Poison events).
        Assert.False(player.IsAlive());
        Assert.False(enemy.IsAlive());
        Assert.Equal(2, recorder.Of<StatusDamage>().Count(d => d.Source == StatusCondition.Poison));

        // The run ends as a loss: exactly one RunEnded, ZERO wins counted despite the enemy also fainting.
        var runEnded = Assert.Single(recorder.Of<RunEnded>());
        Assert.Equal(0, runEnded.BattlesWon);
    }

    // An input that always cancels — stands in for a disconnected client (mirrors SignalRInput.Cancel()).
    private sealed class CancelledInput : IBattleInput
    {
        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            throw new OperationCanceledException("Battle input cancelled (client disconnected).");
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

    // A Normal-type creature whose only move is a 0-damage poison move (Poison status on a Normal-type lands,
    // and a 0-power Normal move clears type immunity). HP/MaxHP are set directly so a single poison tick is
    // lethal — the setup for a deterministic mutual end-of-turn faint.
    private static Creature Poisoner(string name, int maxHp, int hp, int speed)
    {
        var c = new Creature(name)
        {
            Level = 50,
            GrowthRate = GrowthRate.MediumFast,
            Type1 = DamageType.Normal,
        };
        c.CalculateStats();
        c.Experience = c.CalculateExperienceForLevel(50);
        c.Attributes.MaxHP = maxHp;
        c.Attributes.HP = hp;
        c.Attributes.Speed = speed;
        c.AddAttack(
            new Attack
            {
                Name = "poisonpowder",
                BaseDamage = 0,
                Accuracy = 100,
                AttackType = AttackType.Physical,
                PowerPointsMax = 99,
                StatusEffect = StatusCondition.Poison,
                EffectChance = 100,
            }
        );
        return c;
    }
}
