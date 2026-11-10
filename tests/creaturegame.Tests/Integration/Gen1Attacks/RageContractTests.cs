using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Rage (Gen 1): a 20-power Normal attack that, once used, <b>locks the user in</b> (it must keep
/// using Rage every turn) and raises the user's <b>Attack by one stage each time it is hit</b> by a
/// damaging move. The first test drives the on-hit raise through the real <c>AttackAction</c> damage
/// path; the second drives a <b>full <see cref="Battle"/></b> to prove the lock-in (input is consulted
/// once) and that the Attack climbs as the foe lands hits.
/// </summary>
[Collection(MovesCollection.Name)]
public class RageContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task RagingCreatureGainsAnAttackStageWhenHit()
    {
        // The raging creature is the one being hit: a foe's ordinary attack lands on it and its
        // Attack rises by one stage (Gen 1 RageAttackStagesPerHit = 1).
        var rager = TestCreatures.Make("Rager", hp: 500);
        rager.Battle.IsRaging = true;

        var result = await new MoveScenario()
            .Attacker(TestCreatures.Make("Foe"))
            .Defender(rager)
            .Use(Move("pound"));

        Assert.True(result.Has<DamageDealt>(), "the foe's attack lands");
        Assert.Equal(1, rager.Battle.Stages.Attack);

        var change = result.First<StatStageChanged>();
        Assert.NotNull(change);
        Assert.Equal("Rager", change!.CreatureName); // the rager that was hit, not the attacker
        Assert.Equal("Attack", change.Stat);
        Assert.Equal(1, change.Delta);
    }

    [Fact]
    public async Task RageDoesNotRaiseAttackWhenTheAttackerMisses()
    {
        var rager = TestCreatures.Make("Rager", hp: 500);
        rager.Battle.IsRaging = true;

        var result = await new MoveScenario()
            .Rules(NeverHitRules.Instance)
            .Attacker(TestCreatures.Make("Foe"))
            .Defender(rager)
            .Use(Move("pound"));

        Assert.True(result.Has<MoveMissed>());
        Assert.Equal(0, rager.Battle.Stages.Attack); // no hit ⇒ no Rage build-up
    }

    [Fact]
    public async Task RageLocksTheUserInAndClimbsAttackAcrossAFullBattle()
    {
        // Player leads with Rage and outspeeds the enemy. Once used, the player is locked into Rage:
        // Battle force-selects it from RageMove every later turn, so player input is consulted exactly
        // once (on the first turn). The enemy's repeated hits drive the player's Attack up, ramping
        // Rage's damage until the enemy faints.
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = 2000; // tanky enough to survive the whole ramp
        player.Attributes.MaxHP = 2000;
        player.Attributes.Attack = 60; // low, so 20-power Rage needs several (ramping) turns
        player.Attributes.Speed = 200; // outspeed the enemy every turn
        player.AddAttack(Move("rage"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Normal };
        enemy.CalculateStats();
        enemy.Attributes.HP = 150;
        enemy.Attributes.MaxHP = 150;
        enemy.Attributes.Defense = 50; // bulky enough to survive several ramping Rage hits
        enemy.Attributes.Attack = 30; // chips the player without felling it
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(Move("pound"));

        var emitter = new RecordingEmitter();
        var input = new CountingInput(0);
        // Deterministic damage (always hit, no crit, no variance) so the turn count — and therefore
        // the number of hits the enemy lands before fainting — is stable across runs.
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            input,
            AutoSelectInput.Instance,
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter,
            rng: new SeededRandomSource(1)
        );
        await battle.StartFightAsync();

        Assert.False(enemy.IsAlive()); // Rage ramped up and won
        Assert.True(player.Battle.IsRaging); // still locked in at battle's end
        Assert.Equal(1, input.CallCount); // asked only on the first turn — lock-in drives the rest

        // The quirk, not just the outcome: Attack rises exactly once per hit *received*, not once per
        // turn. The enemy doesn't hit on the turn it faints (player outspeeds), so the felling turn
        // contributes neither a hit nor a raise — the two counts stay equal. A bug that raised Attack
        // on the rager's own turn (regardless of being hit) would make raises exceed hits.
        int playerHits = emitter.Of<DamageDealt>().Count(d => d.TargetName == "Player");
        int attackRaises = emitter
            .Of<StatStageChanged>()
            .Count(s => s.CreatureName == "Player" && s.Stat == "Attack");
        Assert.True(playerHits >= 2, "the enemy landed multiple hits before fainting");
        Assert.Equal(playerHits, attackRaises);
    }

    // --- When the lock starts: after the damage, so a miss, an immunity or a Substitute break never locks ---

    private sealed record RageBattle(RecordingEmitter Events, Creature Player)
    {
        public IReadOnlyList<string> PlayerMoves =>
            Events
                .Of<MoveUsed>()
                .Where(m => m.AttackerName == "Player")
                .Select(m => m.MoveName)
                .ToList();
    }

    private async Task<RageBattle> RageThenEmber(
        IBattleRules rules,
        DamageType enemyType = DamageType.Normal,
        bool enemyRaisesSubstitute = false,
        int playerAttack = 100
    )
    {
        var player = TestCreatures.Make(
            "Player",
            hp: 2000,
            attack: playerAttack,
            special: 200,
            speed: enemyRaisesSubstitute ? 1 : 200
        );
        player.AddAttack(Move("rage"));
        player.AddAttack(Move("ember"));

        var enemy = TestCreatures.Make(
            "Enemy",
            type1: enemyType,
            special: 20,
            speed: enemyRaisesSubstitute ? 200 : 1
        );
        enemy.AddAttack(Move("swift"));
        enemy.AddAttack(Move("substitute"));

        var emitter = new RecordingEmitter();
        await new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput("rage", "ember"),
            enemyRaisesSubstitute
                ? new ScriptedInput("substitute", "swift")
                : new ScriptedInput("swift"),
            rules: rules,
            emitter: emitter,
            rng: new SeededRandomSource(1)
        ).StartFightAsync();
        return new RageBattle(emitter, player);
    }

    // Scripts every accuracy roll in order (Swift never rolls).
    private sealed class HitScriptRules(params bool[] hits) : DelegatingBattleRules
    {
        private int _roll;

        public override int GetHitThreshold(int acc, int accStage, int evaStage)
        {
            bool hit = _roll >= hits.Length || hits[_roll];
            _roll++;
            return hit ? 256 : 0;
        }

        public override double GetCritChance(Creature a, Attack m) => 0.0;

        public override double RollDamageVariance() => 1.0;

        public override (double Min, double Max) DamageVarianceRange => (1.0, 1.0);
    }

    [Fact]
    public async Task ARageThatMissesLeavesTheUserFreeToPickItsNextMove()
    {
        var battle = await RageThenEmber(new HitScriptRules(false));

        Assert.Contains(battle.Events.Of<MoveMissed>(), m => m.AttackerName == "Player");
        Assert.Equal(["rage", "ember"], battle.PlayerMoves.Take(2));
        Assert.False(battle.Player.Battle.IsRaging);
    }

    [Fact]
    public async Task ARageIntoAGhostTypeHasNoEffectAndDoesNotLockTheUserIn()
    {
        var battle = await RageThenEmber(NoVarianceNoCritHitRules.Instance, DamageType.Ghost);

        Assert.Contains(battle.Events.Of<MoveHadNoEffect>(), e => e.MoveName == "rage");
        Assert.Equal(["rage", "ember"], battle.PlayerMoves.Take(2));
        Assert.False(battle.Player.Battle.IsRaging);
    }

    [Fact]
    public async Task ARageThatBreaksASubstituteDoesNotLockTheUserIn()
    {
        var battle = await RageThenEmber(
            NoVarianceNoCritHitRules.Instance,
            enemyRaisesSubstitute: true,
            playerAttack: 1000
        );

        Assert.Single(battle.Events.Of<SubstituteBroke>());
        Assert.Equal(["rage", "ember"], battle.PlayerMoves.Take(2));
        Assert.False(battle.Player.Battle.IsRaging);
    }

    [Fact]
    public async Task ARageThatHitsASubstituteThatHoldsLocksTheUserIn()
    {
        var battle = await RageThenEmber(
            NoVarianceNoCritHitRules.Instance,
            enemyRaisesSubstitute: true
        );

        Assert.NotEmpty(battle.Events.Of<SubstituteAbsorbedHit>());
        Assert.All(battle.PlayerMoves, m => Assert.Equal("rage", m));
        Assert.True(battle.Player.Battle.IsRaging);
    }

    [Fact]
    public async Task ARageCopiedByMirrorMoveThatMissesLeavesTheUserFree()
    {
        var player = TestCreatures.Make("Player", hp: 2000, special: 200, speed: 1);
        player.AddAttack(Move("mirror-move"));
        player.AddAttack(Move("ember"));
        var enemy = TestCreatures.Make("Enemy", special: 20, speed: 200);
        enemy.AddAttack(Move("rage"));

        var emitter = new RecordingEmitter();
        await new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            new ScriptedInput("mirror-move", "ember"),
            new ScriptedInput("rage"),
            rules: new HitScriptRules(true, false),
            emitter: emitter,
            rng: new SeededRandomSource(1)
        ).StartFightAsync();

        Assert.Contains(emitter.Of<MoveMissed>(), m => m.AttackerName == "Player");
        Assert.Contains(
            emitter.Of<MoveUsed>(),
            m => m.AttackerName == "Player" && m.MoveName == "ember"
        );
        Assert.False(player.Battle.IsRaging);
    }

    [Fact]
    public async Task ARageThatConnectsLocksTheUserInAndALaterMissKeepsTheLock()
    {
        var battle = await RageThenEmber(new HitScriptRules(true, false));

        Assert.Contains(battle.Events.Of<MoveMissed>(), m => m.AttackerName == "Player");
        Assert.True(battle.PlayerMoves.Count >= 3, "the lock outlasts the missed turn");
        Assert.All(battle.PlayerMoves, m => Assert.Equal("rage", m));
        Assert.True(battle.Player.Battle.IsRaging);
    }

    [Fact]
    public async Task TheRulesCanStartRageBeforeTheHitTest()
    {
        var rules = TestAltProfile.Instance.BuildBattleRules(new SeededRandomSource(0));

        var result = await new MoveScenario()
            .Rules(rules)
            .Attacker(TestCreatures.Make("A"))
            .Defender(TestCreatures.Make("Ghost", type1: DamageType.Ghost))
            .Use(Move("rage"));

        Assert.True(result.Has<MoveHadNoEffect>());
        Assert.True(result.Attacker.Battle.IsRaging);
    }

    /// <summary>Records how many times the engine asked for a move; always picks one fixed index.</summary>
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
