using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Integration;

/// <summary>
/// Drives <see cref="ItemAction"/> through a real <see cref="Battle"/>: the player uses a bag item on a
/// turn, the engine applies the effect, consumes from the bag, and the enemy still attacks. No mocks.
/// </summary>
public class ItemActionBattleTests
{
    // An input that plays a scripted sequence of turn choices (item or move), then defaults to attacking
    // with the first usable move. Enemy side uses ChooseMoveAsync (the default turn action wraps it).
    private sealed class TurnChoiceInput(params TurnChoice[] choices) : IBattleInput
    {
        private readonly Queue<TurnChoice> _choices = new(choices);

        public Task<PokemonAttack> ChooseMoveAsync(TurnContext context) =>
            Task.FromResult(context.Attacker.MoveSet.First(m => m.PowerPointsCurrent > 0));

        public Task<TurnChoice> ChooseTurnActionAsync(TurnContext context) =>
            Task.FromResult(
                _choices.Count > 0
                    ? _choices.Dequeue()
                    : (TurnChoice)
                        new MoveTurnChoice(
                            context.Attacker.MoveSet.First(m => m.PowerPointsCurrent > 0)
                        )
            );
    }

    private static Attack Tackle() =>
        new()
        {
            Name = "tackle",
            BaseDamage = 40,
            PowerPointsMax = 35,
            DamageType = DamageType.Normal,
            AttackType = AttackType.Physical,
        };

    private static Item Potion() =>
        new()
        {
            Id = 17,
            Name = "potion",
            Category = ItemCategory.Healing,
            HealAmount = 20,
        };

    private static async Task<RecordingEmitter> RunAsync(
        IBattleInput playerInput,
        Bag bag,
        Creature player,
        Creature enemy,
        CarriedStatus? entryStatus = null,
        Party? party = null
    )
    {
        var emitter = new RecordingEmitter();
        var battle = new Battle(
            player,
            enemy,
            Gen1TypeChart.Instance,
            playerInput,
            new ScriptedInput("tackle"),
            rules: new ScriptableRules(new SeededRandomSource(1)).Deterministic(),
            emitter: emitter,
            rng: new SeededRandomSource(1),
            playerEntryStatus: entryStatus,
            playerBag: bag,
            playerParty: party
        );
        await battle.StartFightAsync();
        return emitter;
    }

    private static Item Revive() =>
        new()
        {
            Id = 62,
            Name = "revive",
            Category = ItemCategory.Revive,
            RevivePercent = 50,
        };

    [Fact]
    public async Task UsingPotion_HealsConsumesAndAnnounces()
    {
        var player = TestCreatures.Make("Player", hp: 200, speed: 1); // slow
        player.AddAttack(Tackle());
        player.Attributes.ReceiveDamage(80); // HP 120
        var enemy = TestCreatures.Make("Enemy", hp: 60, speed: 200); // fast
        enemy.AddAttack(Tackle());

        var bag = new Bag();
        bag.Add(17, 2);

        var em = await RunAsync(
            new TurnChoiceInput(new ItemTurnChoice(Potion())),
            bag,
            player,
            enemy
        );

        var used = em.Of<ItemUsed>().Single();
        Assert.Equal("potion", used.ItemName);
        Assert.Equal("Player", used.TargetName);
        Assert.Equal(20, em.Of<Healed>().First().HealAmount);
        Assert.Equal(1, bag.Count(17)); // one consumed
    }

    [Fact]
    public async Task ItemResolvesBeforeTheEnemyAttacks()
    {
        // Player is slower than the enemy, so without item priority the enemy would hit first. The heal
        // (turn 1) must still land before the enemy's first hit on the player.
        var player = TestCreatures.Make("Player", hp: 200, speed: 1);
        player.AddAttack(Tackle());
        player.Attributes.ReceiveDamage(80);
        var enemy = TestCreatures.Make("Enemy", hp: 60, speed: 200);
        enemy.AddAttack(Tackle());

        var bag = new Bag();
        bag.Add(17, 1);

        var em = await RunAsync(
            new TurnChoiceInput(new ItemTurnChoice(Potion())),
            bag,
            player,
            enemy
        );

        int healIdx = em.Events.ToList().FindIndex(e => e is Healed);
        int firstHitOnPlayer = em
            .Events.ToList()
            .FindIndex(e => e is DamageDealt d && d.TargetName == "Player");
        Assert.True(healIdx >= 0 && firstHitOnPlayer >= 0);
        Assert.True(healIdx < firstHitOnPlayer, "item heal should resolve before the enemy's hit");
    }

    [Fact]
    public async Task ItemCanBeUsedWhileAsleep()
    {
        // Sleep gates moving, not item use — the engine runs ItemAction without the CanAct check.
        var player = TestCreatures.Make("Player", hp: 200, speed: 100);
        player.AddAttack(Tackle());
        player.Attributes.ReceiveDamage(80);
        var enemy = TestCreatures.Make("Enemy", hp: 60, speed: 50);
        enemy.AddAttack(Tackle());

        var bag = new Bag();
        bag.Add(17, 1);

        var em = await RunAsync(
            new TurnChoiceInput(new ItemTurnChoice(Potion())),
            bag,
            player,
            enemy,
            entryStatus: new CarriedStatus(StatusCondition.Sleep, 5)
        );

        Assert.True(em.Of<ItemUsed>().Any());
        Assert.Equal(20, em.Of<Healed>().First().HealAmount);
    }

    [Fact]
    public async Task UsingDireHit_SetsFocusEnergyAndConsumes()
    {
        // Dire Hit is a BattleStatBoost booster whose effect is the Gen 1 Focus Energy state — driven
        // through a real Battle it sets HasFocusEnergy, emits FocusEnergyApplied, and consumes one.
        var player = TestCreatures.Make("Player", hp: 200, speed: 200);
        player.AddAttack(Tackle());
        var enemy = TestCreatures.Make("Enemy", hp: 60, speed: 1);
        enemy.AddAttack(Tackle());

        var direHit = new Item
        {
            Id = 56,
            Name = "dire-hit",
            Category = ItemCategory.BattleStatBoost,
            BoostsCrit = true,
        };
        var bag = new Bag();
        bag.Add(56, 1);

        var em = await RunAsync(
            new TurnChoiceInput(new ItemTurnChoice(direHit)),
            bag,
            player,
            enemy
        );

        Assert.True(player.Battle.HasFocusEnergy);
        Assert.Equal("Player", em.Of<FocusEnergyApplied>().Single().CreatureName);
        Assert.Equal(0, bag.Count(56)); // consumed
    }

    [Fact]
    public async Task UnusableItem_FailsWithoutConsuming()
    {
        // A Ball has no in-battle effect in this scope — it reports failure and isn't consumed.
        var player = TestCreatures.Make("Player", hp: 200, speed: 200);
        player.AddAttack(Tackle());
        var enemy = TestCreatures.Make("Enemy", hp: 60, speed: 1);
        enemy.AddAttack(Tackle());

        var ball = new Item
        {
            Id = 4,
            Name = "poke-ball",
            Category = ItemCategory.Ball,
        };
        var bag = new Bag();
        bag.Add(4, 1);

        var em = await RunAsync(new TurnChoiceInput(new ItemTurnChoice(ball)), bag, player, enemy);

        Assert.True(em.Of<ItemUseFailed>().Any());
        Assert.False(em.Of<ItemUsed>().Any());
        Assert.Equal(1, bag.Count(4)); // not consumed
    }

    [Fact]
    public async Task UsingRevive_RestoresAFaintedBenchMemberAndConsumes()
    {
        // The player (lead) uses a Revive on turn 1 targeting a fainted bench member, then wins the fight.
        var player = TestCreatures.Make("Player", hp: 200, speed: 200, attack: 999);
        player.AddAttack(Tackle());
        var enemy = TestCreatures.Make("Enemy", hp: 30, speed: 1, defense: 1);
        enemy.AddAttack(Tackle());

        var party = new Party(player);
        var bench = TestCreatures.Make("Bench", hp: 200);
        bench.Attributes.ReceiveDamage(200); // faint the bench member
        party.Add(bench);

        var bag = new Bag();
        bag.Add(62, 1);

        var em = await RunAsync(
            new TurnChoiceInput(new ItemTurnChoice(Revive(), TargetPartySlot: 1)),
            bag,
            player,
            enemy,
            party: party
        );

        // The bench member is back (½ of 200), the active creature/lead is untouched, one revive consumed.
        Assert.True(bench.IsAlive());
        Assert.Equal(100, bench.Attributes.HP);
        Assert.Equal(0, party.LeadIndex);
        Assert.Same(player, party.Lead);
        Assert.Equal(0, bag.Count(62));

        var used = em.Of<ItemUsed>().Single();
        Assert.Equal("revive", used.ItemName);
        Assert.Equal("Bench", used.TargetName); // named the revived member, not the active creature
        Assert.Equal("Bench", em.Of<Revived>().Single().CreatureName);
        Assert.True(em.Of<PartyUpdated>().Any()); // the roster snapshot that repaints the bench bar
    }

    [Fact]
    public async Task UsingRevive_OnANonFaintedMember_FailsWithoutConsuming()
    {
        var player = TestCreatures.Make("Player", hp: 200, speed: 200, attack: 999);
        player.AddAttack(Tackle());
        var enemy = TestCreatures.Make("Enemy", hp: 30, speed: 1, defense: 1);
        enemy.AddAttack(Tackle());

        var party = new Party(player);
        party.Add(TestCreatures.Make("Bench", hp: 200)); // healthy — no valid revive target

        var bag = new Bag();
        bag.Add(62, 1);

        var em = await RunAsync(
            new TurnChoiceInput(new ItemTurnChoice(Revive(), TargetPartySlot: 1)),
            bag,
            player,
            enemy,
            party: party
        );

        Assert.True(em.Of<ItemUseFailed>().Any());
        Assert.False(em.Of<Revived>().Any());
        Assert.Equal(1, bag.Count(62)); // not consumed
    }
}
