using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;
using static creaturegame.Tests.TestSupport.TestFighters;

namespace creaturegame.Tests.Integration.Flow;

/// <summary>Which states keep the battle menu (<see cref="IBattleRules.SkipsBattleMenu"/>); GENERATION_SEAMS.md §2.</summary>
public class BattleMenuUnderLockInTests
{
    // ConsultedTurns are exactly the turns the menu was shown.
    private sealed class PlannedInput(
        string fallbackMove,
        params Func<TurnContext, TurnChoice>[] plan
    ) : IBattleInput
    {
        private readonly Queue<Func<TurnContext, TurnChoice>> _plan = new(plan);
        public List<int> ConsultedTurns { get; } = [];

        public Task<TurnChoice> ChooseTurnActionAsync(TurnContext context)
        {
            ConsultedTurns.Add(context.TurnNumber);
            return Task.FromResult(
                _plan.Count > 0 ? _plan.Dequeue()(context) : Fight(fallbackMove)(context)
            );
        }

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            Task.FromResult(context.Attacker.MoveSet.First(m => m.Base.Name == fallbackMove));
    }

    private static Func<TurnContext, TurnChoice> Fight(string move) =>
        c => new MoveTurnChoice(c.Attacker.MoveSet.First(m => m.Base.Name == move));

    private static Func<TurnContext, TurnChoice> UsePotion() => _ => new ItemTurnChoice(Potion());

    private static Func<TurnContext, TurnChoice> SwitchTo(int index) =>
        _ => new SwitchTurnChoice(index);

    private sealed class EveryLockInSkipsTheMenu()
        : DelegatingBattleRules(new SeededRandomSource(1))
    {
        public override bool SkipsBattleMenu(Creature creature) =>
            TestAltProfile.EveryLockInSkipsTheMenu(creature);
    }

    [Fact]
    public async Task ABideUserGetsTheMenu_AndAnItemTurnDelaysTheRelease()
    {
        var player = Fighter(
            "Player",
            hp: 2000,
            attack: 50,
            defense: 100,
            speed: 200,
            tackleId: 33
        );
        player.AddAttack(Bide());
        var enemy = Fighter("Foe", hp: 2000, attack: 60, defense: 100, speed: 1, tackleId: 33);
        var bag = new Bag();
        bag.Add(17);
        var input = new PlannedInput("tackle", Fight("bide"), UsePotion(), Fight("tackle"));

        var (recorder, _) = await RunAsync(
            player,
            enemy,
            input,
            new ScriptableRules().Deterministic().BideTurns(2),
            bag: bag,
            turns: 3
        );

        Assert.Equal([1, 2, 3], input.ConsultedTurns.Take(3));
        Assert.Single(recorder.Of<BideStoring>());
        Assert.Equal(1, TurnOf(recorder, e => e is ItemUsed));
        Assert.Equal(2, TurnOf(recorder, e => e is DamageDealt { TargetName: "Foe" }));
    }

    [Fact]
    public async Task ABinderUsesAPotion_TheFoeCantMove_AndTheTrapEndsOnSchedule()
    {
        var player = Fighter("Player", hp: 300, attack: 50, defense: 999, speed: 200, tackleId: 33);
        player.AddAttack(Wrap());
        player.Attributes.ReceiveDamage(50);
        var enemy = Fighter("Foe", hp: 2000, attack: 50, defense: 999, speed: 1, tackleId: 33);
        var bag = new Bag();
        bag.Add(17);
        var input = new PlannedInput("tackle", Fight("wrap"), UsePotion(), Fight("tackle"));

        var (recorder, _) = await RunAsync(
            player,
            enemy,
            input,
            new ScriptableRules().Deterministic().BindingTurns(3),
            bag: bag,
            turns: 4
        );

        var blocked = TurnsOf(recorder, e => e is BindingBlocked { CreatureName: "Foe" });
        Assert.Equal([0, 1, 2], blocked);
        Assert.Equal(1, TurnOf(recorder, e => e is ItemUsed));
        Assert.Equal(2, TurnOf(recorder, e => e is BindingContinues));
        Assert.Equal(3, TurnOf(recorder, e => e is MoveUsed { AttackerName: "Foe" }));
    }

    [Fact]
    public async Task ABinderUsesAPotionOnTheLastTrapTurn_TheFoeMovesThatTurn()
    {
        var (recorder, _) = await PotionOnTheLastTrapTurnAsync(new ScriptableRules());

        Assert.Equal([0], TurnsOf(recorder, e => e is BindingBlocked { CreatureName: "Foe" }));
        Assert.Equal(1, TurnOf(recorder, e => e is ItemUsed));
        Assert.Equal(1, TurnOf(recorder, e => e is MoveUsed { AttackerName: "Foe" }));
    }

    [Fact]
    public async Task UnderRulesThatCountTheTrapDownAtTurnEnd_APotionOnTheLastTurnKeepsTheFoeHeld()
    {
        var (recorder, _) = await PotionOnTheLastTrapTurnAsync(
            new ScriptableRules().TrapCountsDownAtTurnEndOnly()
        );

        Assert.Equal([0, 1], TurnsOf(recorder, e => e is BindingBlocked { CreatureName: "Foe" }));
        Assert.Equal(2, TurnOf(recorder, e => e is MoveUsed { AttackerName: "Foe" }));
    }

    private static Task<(
        StopAfterTurnEmitter Recorder,
        Battle Battle
    )> PotionOnTheLastTrapTurnAsync(ScriptableRules rules)
    {
        var player = Fighter("Player", hp: 300, attack: 50, defense: 999, speed: 200, tackleId: 33);
        player.AddAttack(Wrap());
        player.Attributes.ReceiveDamage(50);
        var enemy = Fighter("Foe", hp: 2000, attack: 50, defense: 999, speed: 1, tackleId: 33);
        var bag = new Bag();
        bag.Add(17);
        var input = new PlannedInput("tackle", Fight("wrap"), UsePotion(), Fight("tackle"));
        return RunAsync(
            player,
            enemy,
            input,
            rules.Deterministic().BindingTurns(2),
            bag: bag,
            turns: 3
        );
    }

    [Fact]
    public async Task FightUnderAWrapContinuesTheWrap_WhicheverMoveIsPicked()
    {
        var player = Fighter("Player", hp: 300, attack: 50, defense: 999, speed: 200, tackleId: 33);
        player.AddAttack(Wrap());
        var bench = Fighter("Bench", hp: 300, attack: 50, defense: 999, speed: 100, tackleId: 33);
        var party = new Party(player);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 2000, attack: 50, defense: 999, speed: 1, tackleId: 33);
        var input = new PlannedInput("tackle", Fight("wrap"), Fight("tackle"));

        var (recorder, _) = await RunAsync(
            player,
            enemy,
            input,
            new ScriptableRules().Deterministic().BindingTurns(5),
            party: party,
            turns: 2
        );

        var turnTwo = recorder.Of<TurnStarted>().First(t => t.TurnNumber == 2);
        Assert.True(turnTwo.FightContinuesLock);
        Assert.True(turnTwo.CanSwitch);
        Assert.Equal(1, TurnOf(recorder, e => e is BindingContinues));
        Assert.DoesNotContain(
            recorder.Of<MoveUsed>(),
            m => m.AttackerName == "Player" && m.MoveName == "tackle"
        );
    }

    [Fact]
    public async Task ABinderOutOfPpThatPicksFight_ContinuesTheWrapRatherThanStruggling()
    {
        var player = Fighter("Player", hp: 300, attack: 50, defense: 999, speed: 200, tackleId: 33);
        player.AddAttack(Wrap());
        foreach (var m in player.MoveSet)
            m.PowerPointsCurrent = m.Base.Name == "wrap" ? 1 : 0;
        var enemy = Fighter("Foe", hp: 2000, attack: 50, defense: 999, speed: 1, tackleId: 33);
        var input = new PlannedInput("wrap", Fight("wrap"), _ => StruggleTurnChoice.Instance);

        var (recorder, _) = await RunAsync(
            player,
            enemy,
            input,
            new ScriptableRules().Deterministic().BindingTurns(5),
            turns: 2
        );

        Assert.Equal(1, TurnOf(recorder, e => e is BindingContinues));
        Assert.DoesNotContain(
            recorder.Of<MoveUsed>(),
            m => m.AttackerName == "Player" && m.MoveName == "Struggle"
        );
    }

    [Fact]
    public async Task ABinderThatSwitchesOut_FreesTheFoe()
    {
        var player = Fighter("Player", hp: 300, attack: 50, defense: 999, speed: 200, tackleId: 33);
        player.AddAttack(Wrap());
        var bench = Fighter("Bench", hp: 300, attack: 50, defense: 999, speed: 100, tackleId: 33);
        var party = new Party(player);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 2000, attack: 50, defense: 999, speed: 1, tackleId: 33);
        var input = new PlannedInput("tackle", Fight("wrap"), SwitchTo(1));

        var (recorder, _) = await RunAsync(
            player,
            enemy,
            input,
            new ScriptableRules().Deterministic().BindingTurns(5),
            party: party,
            turns: 2
        );

        Assert.Equal(1, TurnOf(recorder, e => e is CreatureSwitchedIn { Name: "Bench" }));
        Assert.Equal(1, TurnOf(recorder, e => e is MoveUsed { AttackerName: "Foe" }));
        Assert.Equal([0], TurnsOf(recorder, e => e is BindingBlocked { CreatureName: "Foe" }));
    }

    [Fact]
    public async Task AHyperBeamUserGetsNoMenuOnItsRechargeTurn()
    {
        var player = Fighter("Player", hp: 300, attack: 40, defense: 999, speed: 200, tackleId: 33);
        player.AddAttack(HyperBeam());
        var bench = Fighter("Bench", hp: 300, attack: 40, defense: 999, speed: 150, tackleId: 33);
        var party = new Party(player);
        party.Add(bench);
        var enemy = Fighter("Foe", hp: 2000, attack: 40, defense: 100, speed: 100, tackleId: 33);
        var input = new PlannedInput("tackle", Fight("hyperbeam"), SwitchTo(1));

        var (recorder, _) = await RunAsync(
            player,
            enemy,
            input,
            new ScriptableRules().Deterministic(),
            party: party,
            turns: 3
        );

        Assert.DoesNotContain(2, input.ConsultedTurns);
        Assert.False(recorder.Of<TurnStarted>().First(t => t.TurnNumber == 2).CanSwitch);
        Assert.Equal(1, TurnOf(recorder, e => e is Recharging { CreatureName: "Player" }));
        Assert.DoesNotContain(1, TurnsOf(recorder, e => e is MoveUsed { AttackerName: "Player" }));
        Assert.Equal(2, TurnOf(recorder, e => e is CreatureSwitchedIn { Name: "Bench" }));
    }

    [Fact]
    public async Task UnderRulesWhereEveryLockInSkipsTheMenu_ABideUserIsNeverAsked()
    {
        var player = Fighter(
            "Player",
            hp: 2000,
            attack: 50,
            defense: 100,
            speed: 200,
            tackleId: 33
        );
        player.AddAttack(Bide());
        var enemy = Fighter("Foe", hp: 2000, attack: 60, defense: 100, speed: 1, tackleId: 33);
        var input = new PlannedInput("tackle", Fight("bide"));

        var (recorder, _) = await RunAsync(
            player,
            enemy,
            input,
            new EveryLockInSkipsTheMenu(),
            turns: 2
        );

        Assert.Equal(1, input.ConsultedTurns[0]);
        Assert.DoesNotContain(2, input.ConsultedTurns);
        var turnTwo = recorder.Of<TurnStarted>().First(t => t.TurnNumber == 2);
        Assert.False(turnTwo.FightContinuesLock);
    }

    // Runs the battle through the given number of turns; the emitter throws at the next TurnStarted.
    private static async Task<(StopAfterTurnEmitter Recorder, Battle Battle)> RunAsync(
        Creature player,
        Creature enemy,
        PlannedInput input,
        IBattleRules rules,
        Bag? bag = null,
        Party? party = null,
        int turns = 3
    )
    {
        var recorder = new StopAfterTurnEmitter(turns);
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            input,
            new ScriptedInput("tackle"),
            rules: rules,
            emitter: recorder,
            rng: new SeededRandomSource(0),
            playerBag: bag,
            playerParty: party
        );
        try
        {
            await battle.StartFightAsync();
        }
        catch (StopAfterTurnEmitter.Stop) { }
        return (recorder, battle);
    }

    // Records like RecordingEmitter, and ends the battle at the TurnStarted that opens the turn after the last one.
    private sealed class StopAfterTurnEmitter(int lastTurn) : IBattleEventEmitter
    {
        public sealed class Stop : Exception;

        private readonly List<BattleEvent> _events = [];
        public IReadOnlyList<BattleEvent> Events => _events;

        public void Emit(BattleEvent evt)
        {
            if (evt is TurnStarted { TurnNumber: var n } && n > lastTurn)
                throw new Stop();
            _events.Add(evt);
        }

        public IEnumerable<T> Of<T>() => _events.OfType<T>();
    }

    // Zero-based index of the turn an event fell in (0 = turn 1), counted by the TurnEnded markers before it.
    private static int TurnOf(StopAfterTurnEmitter recorder, Func<BattleEvent, bool> match) =>
        TurnsOf(recorder, match).First();

    private static List<int> TurnsOf(StopAfterTurnEmitter recorder, Func<BattleEvent, bool> match)
    {
        var turns = new List<int>();
        int turn = 0;
        foreach (var e in recorder.Events)
        {
            if (e is TurnEnded)
                turn++;
            else if (match(e))
                turns.Add(turn);
        }
        return turns;
    }

    private static Item Potion() =>
        new()
        {
            Id = 17,
            Name = "potion",
            Category = ItemCategory.Healing,
            HealAmount = 20,
        };

    private static Attack Bide() =>
        new()
        {
            Id = 117,
            Name = "bide",
            BaseDamage = 0,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            Effect = MoveEffect.Bide,
        };

    private static Attack Wrap() =>
        new()
        {
            Id = 35,
            Name = "wrap",
            BaseDamage = 15,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            Effect = MoveEffect.Binding,
        };

    private static Attack HyperBeam() =>
        new()
        {
            Id = 63,
            Name = "hyperbeam",
            BaseDamage = 150,
            Accuracy = 100,
            AttackType = AttackType.Physical,
            PowerPointsMax = 99,
            Effect = MoveEffect.Recharge,
        };
}
