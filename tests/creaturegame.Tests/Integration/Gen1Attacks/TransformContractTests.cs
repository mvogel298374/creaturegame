using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration.Gen1Attacks;

/// <summary>
/// Transform (Gen 1): the user becomes a copy of the target — its types, the four non-HP battle stats,
/// current stat stages, SpeciesId and full moveset (each copied move at 5 PP, capped at the move's max).
/// HP, MaxHP and level stay the user's. The change is undone when the battle ends (and on any mid-battle
/// <see cref="Creature.ResetBattleState"/>, e.g. Haze), so it never leaks into the permanent Creature.
/// </summary>
[Collection(MovesCollection.Name)]
public class TransformContractTests(MovesFixture moves) : Gen1MoveContract(moves)
{
    [Fact]
    public async Task TransformCopiesTheTargetsTypesAndNonHpStatsButKeepsOwnHp()
    {
        var attacker = TestCreatures.Make(
            "A",
            type1: DamageType.Normal,
            type2: null,
            hp: 300,
            attack: 50,
            defense: 50,
            special: 50,
            speed: 50
        );
        var target = TestCreatures.Make(
            "B",
            type1: DamageType.Water,
            type2: DamageType.Ice,
            hp: 120,
            attack: 222,
            defense: 188,
            special: 199,
            speed: 175
        );
        target.SpeciesId = 134; // Vaporeon — Transform copies the species id (it drives the client sprite morph)

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(target)
            .Use(Move("transform"));

        Assert.False(result.Has<DamageDealt>(), "Transform deals no damage");
        Assert.NotNull(result.First<TransformedInto>());
        // The copied species id rides on the event (the client morphs the transforming sprite to it) and is
        // applied to the user.
        Assert.Equal(134, result.First<TransformedInto>()!.IntoSpeciesId);
        Assert.Equal(134, attacker.SpeciesId);
        // Transform is self-affecting: it must never apply a status to the foe (guards the pre-handler
        // TryApplyStatus path — the StatusEffect==None data pin's behavioural twin).
        Assert.Equal(StatusCondition.None, result.Defender.Battle.Status);

        // Types and the four non-HP stats are now the target's.
        Assert.Equal(DamageType.Water, attacker.Type1);
        Assert.Equal(DamageType.Ice, attacker.Type2);
        Assert.Equal(222, attacker.Attributes.Attack);
        Assert.Equal(188, attacker.Attributes.Defense);
        Assert.Equal(199, attacker.Attributes.Special);
        Assert.Equal(175, attacker.Attributes.Speed);

        // HP and MaxHP are the user's own — Transform never copies them.
        Assert.Equal(300, attacker.Attributes.MaxHP);
        Assert.Equal(300, attacker.Attributes.HP);
    }

    [Fact]
    public async Task TransformCopiesTheTargetsMovesetAtFivePp()
    {
        var attacker = TestCreatures.Make("A");
        var target = TestCreatures.Make("B");
        target.AddAttack(Move("tackle")); // PP 35 → capped to 5
        target.AddAttack(Move("swords-dance")); // PP 30 → capped to 5

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(target)
            .Use(Move("transform"));

        var copied = result.Attacker.MoveSet;
        Assert.Equal(new[] { "tackle", "swords-dance" }, copied.Select(m => m.Base.Name));
        Assert.All(copied, m => Assert.Equal(5, m.PowerPointsCurrent));
        // It's a copy, not the same wrapper instance — spending the user's PP must not touch the target's.
        Assert.NotSame(target.MoveSet[0], copied[0]);
    }

    [Fact]
    public async Task TransformCopiesTheTargetsStatStagesIndependently()
    {
        var attacker = TestCreatures.Make("A");
        var target = TestCreatures.Make("B");
        target.Battle.Stages.RaiseAttack(2);
        target.Battle.Stages.RaiseSpeed(-1);

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(target)
            .Use(Move("transform"));

        Assert.Equal(2, result.Attacker.Battle.Stages.Attack);
        Assert.Equal(-1, result.Attacker.Battle.Stages.Speed);

        // The copy is independent — raising the user's stage doesn't bleed back into the target.
        result.Attacker.Battle.Stages.RaiseAttack(1);
        Assert.Equal(3, result.Attacker.Battle.Stages.Attack);
        Assert.Equal(2, target.Battle.Stages.Attack);
    }

    [Fact]
    public async Task ResettingBattleStateRevertsATransform()
    {
        // Guards the Haze interaction: ResetBattleState() must restore the original identity (types,
        // stats, moveset) before swapping in a fresh BattleState, or the copied identity leaks into the
        // permanent half of a reused Creature.
        var attacker = TestCreatures.Make("A", type1: DamageType.Normal, attack: 50);
        attacker.AddAttack(Move("pound"));
        var target = TestCreatures.Make("B", type1: DamageType.Ghost, attack: 200);
        target.AddAttack(Move("tackle"));

        var result = await new MoveScenario()
            .Attacker(attacker)
            .Defender(target)
            .Use(Move("transform"));
        Assert.Equal(DamageType.Ghost, attacker.Type1); // transformed
        Assert.Equal(200, attacker.Attributes.Attack);

        attacker.ResetBattleState();

        Assert.Equal(DamageType.Normal, attacker.Type1); // reverted, not orphaned
        Assert.Equal(50, attacker.Attributes.Attack);
        // The harness adds "transform" to the attacker when it's used, so the pre-Transform moveset
        // snapshot (restored here) is the original "pound" plus that "transform" slot — Tackle (copied
        // from the target) is gone.
        Assert.Equal(new[] { "pound", "transform" }, attacker.MoveSet.Select(m => m.Base.Name));
    }

    [Fact]
    public async Task TransformRevertsWhenTheBattleEnds()
    {
        // Player transforms into the enemy on turn 1 (copying its Water type, 200 Attack and Quick
        // Attack), then on turn 2 the copied Quick Attack — now backed by the enemy's 200 Attack —
        // finishes the near-dead enemy. When the battle ends the player's identity must revert to its
        // original Normal type / 60 Attack / lone Transform move — never leaking the enemy's identity.
        //
        // Determinism: the battle RNG is seeded AND the player's Defense is pinned. Defense is otherwise
        // left to the random DV roll in CalculateStats, and the enemy's Quick Attack has +1 priority, so
        // an unpinned low-Defense roll let the enemy one-shot the 500-HP player on turn 1 before it could
        // Transform — the source of this test's former ~1-in-5 flake. The enemy is Water (not Ghost): a
        // Ghost enemy is immune to the copied Normal-type Quick Attack (0×), so the old test only "won"
        // by stalling ~40 turns until the enemy fainted on its own Struggle recoil — a false premise.
        var player = new Creature("Player") { Level = 50, Type1 = DamageType.Normal };
        player.CalculateStats();
        player.Attributes.HP = player.Attributes.MaxHP = 500;
        player.Attributes.Attack = 60;
        player.Attributes.Defense = 100; // pin: don't let the +1-priority enemy randomly OHKO the player
        player.Attributes.Speed = 250; // outspeed so Transform lands first
        player.AddAttack(Move("transform"));

        var enemy = new Creature("Enemy") { Level = 50, Type1 = DamageType.Water };
        enemy.CalculateStats();
        enemy.Attributes.HP = enemy.Attributes.MaxHP = 6;
        enemy.Attributes.Attack = 200;
        enemy.Attributes.Defense = 10;
        enemy.Attributes.Speed = 1;
        enemy.AddAttack(Move("quick-attack")); // priority move the player copies and then KOs with

        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            AutoSelectInput.Instance,
            AutoSelectInput.Instance,
            rules: NoVarianceNoCritHitRules.Instance,
            emitter: emitter,
            rng: new SeededRandomSource(0)
        );
        await battle.StartFightAsync();

        Assert.NotNull(emitter.Of<TransformedInto>().FirstOrDefault());
        Assert.False(enemy.IsAlive());

        // Identity restored at battle end.
        Assert.Equal(DamageType.Normal, player.Type1);
        Assert.Equal(60, player.Attributes.Attack);
        Assert.Equal(new[] { "transform" }, player.MoveSet.Select(m => m.Base.Name));
        Assert.Null(player.Battle.OriginalIdentity);
        // HP was never copied — the player keeps its own (minus whatever the enemy managed to deal).
        Assert.Equal(500, player.Attributes.MaxHP);
    }
}
