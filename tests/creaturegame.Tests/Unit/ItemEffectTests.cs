using creaturegame.Attacks;
using creaturegame.Combat;
using creaturegame.Creatures;
using creaturegame.Items;
using creaturegame.Tests.TestSupport;

namespace creaturegame.Tests.Unit;

/// <summary>
/// Pure tests of the in-battle item effects (<see cref="ItemEffects"/>) and the <see cref="Bag"/> — no
/// Battle, no DB. Each effect drives a real <see cref="Creature"/> and the resolved Gen-1 item data.
/// </summary>
public class ItemEffectTests
{
    private static Item Item(
        int id,
        string name,
        ItemCategory category,
        int? heal = null,
        bool healsAll = false,
        bool curesAll = false,
        StatusCondition? cured = null,
        int? pp = null,
        bool restoresAllPp = false,
        bool ppAllMoves = false,
        StageStat? boostStat = null,
        int? boostStages = null,
        bool boostsCrit = false,
        bool setsMist = false,
        int? revivePercent = null
    ) =>
        new()
        {
            Id = id,
            Name = name,
            Category = category,
            HealAmount = heal,
            HealsAllHp = healsAll,
            CuresAllStatus = curesAll,
            CuredStatus = cured,
            PpRestoreAmount = pp,
            RestoresAllPp = restoresAllPp,
            RestoresPpAllMoves = ppAllMoves,
            StatBoostStat = boostStat,
            StatBoostStages = boostStages,
            BoostsCrit = boostsCrit,
            SetsMist = setsMist,
            RevivePercent = revivePercent,
        };

    private static Attack Move(string name, int id, int pp = 20) =>
        new()
        {
            Id = id,
            Name = name,
            PowerPointsMax = pp,
            BaseDamage = 40,
        };

    private static (Creature, RecordingEmitter) Apply(Item item, Creature user, int? slot = null)
    {
        var emitter = new RecordingEmitter();
        var effect = ItemEffects.For(item.Category)!;
        var ctx = new ItemEffectContext
        {
            User = user,
            Item = item,
            TargetMoveSlot = slot,
            Emitter = emitter,
        };
        Assert.True(effect.CanApply(ctx));
        effect.Apply(ctx);
        return (user, emitter);
    }

    private static bool CanApply(Item item, Creature user, int? slot = null) =>
        ItemEffects
            .For(item.Category)!
            .CanApply(
                new ItemEffectContext
                {
                    User = user,
                    Item = item,
                    TargetMoveSlot = slot,
                }
            );

    // ── Bag ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Bag_AddCountConsume()
    {
        var bag = new Bag();
        Assert.False(bag.Has(7));
        bag.Add(7, 2);
        Assert.Equal(2, bag.Count(7));
        Assert.True(bag.Consume(7));
        Assert.Equal(1, bag.Count(7));
        Assert.True(bag.Consume(7));
        Assert.Equal(0, bag.Count(7));
        Assert.False(bag.Consume(7)); // nothing left
    }

    // ── Registry ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ItemCategory.Healing)]
    [InlineData(ItemCategory.StatusCure)]
    [InlineData(ItemCategory.PpRestore)]
    [InlineData(ItemCategory.BattleStatBoost)]
    [InlineData(ItemCategory.Revive)] // now targets a fainted party member
    public void Registry_HasEffectForInScopeCategories(ItemCategory category) =>
        Assert.NotNull(ItemEffects.For(category));

    [Theory]
    [InlineData(ItemCategory.Ball)] // catch — deferred
    [InlineData(ItemCategory.Other)]
    public void Registry_NoEffectForDeferredCategories(ItemCategory category) =>
        Assert.Null(ItemEffects.For(category));

    // ── Healing ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Potion_RestoresFixedHp()
    {
        var c = TestCreatures.Make(hp: 200);
        c.Attributes.ReceiveDamage(80); // HP 120
        var (user, em) = Apply(Item(17, "potion", ItemCategory.Healing, heal: 20), c);

        Assert.Equal(140, user.Attributes.HP);
        Assert.Equal(20, em.Of<Healed>().Single().HealAmount);
    }

    [Fact]
    public void Potion_AtFullHp_HasNoEffect()
    {
        var c = TestCreatures.Make(hp: 200); // full
        Assert.False(CanApply(Item(17, "potion", ItemCategory.Healing, heal: 20), c));
    }

    [Fact]
    public void Potion_OverhealCapsAtMax()
    {
        var c = TestCreatures.Make(hp: 200);
        c.Attributes.ReceiveDamage(5); // HP 195
        var (user, em) = Apply(Item(17, "potion", ItemCategory.Healing, heal: 20), c);

        Assert.Equal(200, user.Attributes.HP);
        Assert.Equal(5, em.Of<Healed>().Single().HealAmount); // reports actual restored, not 20
    }

    [Fact]
    public void MaxPotion_RestoresToFull()
    {
        var c = TestCreatures.Make(hp: 200);
        c.Attributes.ReceiveDamage(150); // HP 50
        var (user, _) = Apply(Item(24, "max-potion", ItemCategory.Healing, healsAll: true), c);
        Assert.Equal(200, user.Attributes.HP);
    }

    [Fact]
    public void FullRestore_HealsAndCuresStatus()
    {
        var c = TestCreatures.Make(hp: 200);
        c.Attributes.ReceiveDamage(100);
        c.Battle.Status = StatusCondition.Poison;
        var (user, em) = Apply(
            Item(23, "full-restore", ItemCategory.Healing, healsAll: true, curesAll: true),
            c
        );

        Assert.Equal(200, user.Attributes.HP);
        Assert.Equal(StatusCondition.None, user.Battle.Status);
        Assert.Equal(StatusCondition.Poison, em.Of<StatusCleared>().Single().WasStatus);
    }

    // ── Status cures ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Antidote_CuresPoison()
    {
        var c = TestCreatures.Make();
        c.Battle.Status = StatusCondition.Poison;
        var (user, em) = Apply(
            Item(18, "antidote", ItemCategory.StatusCure, cured: StatusCondition.Poison),
            c
        );
        Assert.Equal(StatusCondition.None, user.Battle.Status);
        Assert.Equal(StatusCondition.Poison, em.Of<StatusCleared>().Single().WasStatus);
    }

    [Fact]
    public void Antidote_AlsoCuresBadPoison()
    {
        var c = TestCreatures.Make();
        c.Battle.Status = StatusCondition.BadPoison;
        c.Battle.ToxicCounter = 4;
        var (user, _) = Apply(
            Item(18, "antidote", ItemCategory.StatusCure, cured: StatusCondition.Poison),
            c
        );
        Assert.Equal(StatusCondition.None, user.Battle.Status);
        Assert.Equal(1, user.Battle.ToxicCounter); // escalation reset
    }

    [Fact]
    public void Antidote_OnWrongStatus_HasNoEffect()
    {
        var c = TestCreatures.Make();
        c.Battle.Status = StatusCondition.Burn;
        Assert.False(
            CanApply(
                Item(18, "antidote", ItemCategory.StatusCure, cured: StatusCondition.Poison),
                c
            )
        );
    }

    [Fact]
    public void Awakening_CuresSleepAndResetsCounter()
    {
        var c = TestCreatures.Make();
        c.Battle.Status = StatusCondition.Sleep;
        c.Battle.SleepTurns = 3;
        var (user, _) = Apply(
            Item(21, "awakening", ItemCategory.StatusCure, cured: StatusCondition.Sleep),
            c
        );
        Assert.Equal(StatusCondition.None, user.Battle.Status);
        Assert.Equal(0, user.Battle.SleepTurns);
    }

    [Fact]
    public void FullHeal_CuresAnyStatus()
    {
        var c = TestCreatures.Make();
        c.Battle.Status = StatusCondition.Paralysis;
        var (user, _) = Apply(Item(27, "full-heal", ItemCategory.StatusCure, curesAll: true), c);
        Assert.Equal(StatusCondition.None, user.Battle.Status);
    }

    [Fact]
    public void StatusCure_WithNoStatus_HasNoEffect()
    {
        var c = TestCreatures.Make(); // healthy
        Assert.False(CanApply(Item(27, "full-heal", ItemCategory.StatusCure, curesAll: true), c));
    }

    // ── PP restore ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ether_RestoresOneMove()
    {
        var c = TestCreatures.Make();
        c.AddAttack(Move("tackle", 1, pp: 35));
        c.AddAttack(Move("growl", 2, pp: 40));
        c.MoveSet[0].PowerPointsCurrent = 5;
        c.MoveSet[1].PowerPointsCurrent = 10;

        var (user, em) = Apply(Item(38, "ether", ItemCategory.PpRestore, pp: 10), c, slot: 0);

        Assert.Equal(15, user.MoveSet[0].PowerPointsCurrent); // +10
        Assert.Equal(10, user.MoveSet[1].PowerPointsCurrent); // untouched
        Assert.Equal("tackle", em.Of<PpRestored>().Single().MoveName);
    }

    [Fact]
    public void Ether_OnFullMove_HasNoEffect()
    {
        var c = TestCreatures.Make();
        c.AddAttack(Move("tackle", 1, pp: 35)); // full
        Assert.False(CanApply(Item(38, "ether", ItemCategory.PpRestore, pp: 10), c, slot: 0));
    }

    [Fact]
    public void Elixir_RestoresAllMoves()
    {
        var c = TestCreatures.Make();
        c.AddAttack(Move("tackle", 1, pp: 35));
        c.AddAttack(Move("growl", 2, pp: 40));
        c.MoveSet[0].PowerPointsCurrent = 5;
        c.MoveSet[1].PowerPointsCurrent = 0;

        var (user, em) = Apply(
            Item(40, "elixir", ItemCategory.PpRestore, pp: 10, ppAllMoves: true),
            c
        );

        Assert.Equal(15, user.MoveSet[0].PowerPointsCurrent);
        Assert.Equal(10, user.MoveSet[1].PowerPointsCurrent);
        Assert.Equal(2, em.Of<PpRestored>().Count());
    }

    [Fact]
    public void MaxElixir_RestoresAllMovesToFull()
    {
        var c = TestCreatures.Make();
        c.AddAttack(Move("tackle", 1, pp: 35));
        c.AddAttack(Move("growl", 2, pp: 40));
        c.MoveSet[0].PowerPointsCurrent = 1;
        c.MoveSet[1].PowerPointsCurrent = 2;

        var (user, _) = Apply(
            Item(41, "max-elixir", ItemCategory.PpRestore, restoresAllPp: true, ppAllMoves: true),
            c
        );

        Assert.Equal(35, user.MoveSet[0].PowerPointsCurrent);
        Assert.Equal(40, user.MoveSet[1].PowerPointsCurrent);
    }

    // ── X-items ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void XAttack_RaisesAttackStage()
    {
        var c = TestCreatures.Make();
        var (user, em) = Apply(
            Item(
                57,
                "x-attack",
                ItemCategory.BattleStatBoost,
                boostStat: StageStat.Attack,
                boostStages: 1
            ),
            c
        );

        Assert.Equal(1, user.Battle.Stages.Attack);
        var ev = em.Of<StatStageChanged>().Single();
        Assert.Equal("Attack", ev.Stat);
        Assert.Equal(1, ev.NewStage);
    }

    [Fact]
    public void XItem_AtMaxStage_HasNoEffect()
    {
        var c = TestCreatures.Make();
        c.Battle.Stages.RaiseAttack(6);
        Assert.False(
            CanApply(
                Item(
                    57,
                    "x-attack",
                    ItemCategory.BattleStatBoost,
                    boostStat: StageStat.Attack,
                    boostStages: 1
                ),
                c
            )
        );
    }

    [Fact]
    public void DireHit_SetsFocusEnergy()
    {
        // Dire Hit raises crit via the Gen 1 Focus Energy state (and reuses its event).
        var c = TestCreatures.Make();
        var (user, em) = Apply(
            Item(56, "dire-hit", ItemCategory.BattleStatBoost, boostsCrit: true),
            c
        );

        Assert.True(user.Battle.HasFocusEnergy);
        Assert.Equal(user.Name, em.Of<FocusEnergyApplied>().Single().CreatureName);
    }

    [Fact]
    public void DireHit_WhenAlreadyFocused_HasNoEffect()
    {
        var c = TestCreatures.Make();
        c.Battle.HasFocusEnergy = true;
        Assert.False(
            CanApply(Item(56, "dire-hit", ItemCategory.BattleStatBoost, boostsCrit: true), c)
        );
    }

    [Fact]
    public void GuardSpec_SetsMist()
    {
        // Guard Spec. shrouds the user in Mist (foe can't lower its stats) — reuses the Mist move's event.
        var c = TestCreatures.Make();
        var (user, em) = Apply(
            Item(55, "guard-spec", ItemCategory.BattleStatBoost, setsMist: true),
            c
        );

        Assert.True(user.Battle.HasMist);
        Assert.Equal(user.Name, em.Of<MistApplied>().Single().CreatureName);
    }

    [Fact]
    public void GuardSpec_WhenAlreadyMisted_HasNoEffect()
    {
        var c = TestCreatures.Make();
        c.Battle.HasMist = true;
        Assert.False(
            CanApply(Item(55, "guard-spec", ItemCategory.BattleStatBoost, setsMist: true), c)
        );
    }

    // ── Revive (targets a fainted PARTY member, not the active creature) ──────────────────────────

    // Revive resolves against Party + TargetPartySlot rather than User, so it gets its own harness.
    private static (Party, RecordingEmitter) ApplyRevive(Item item, Party party, int slot)
    {
        var emitter = new RecordingEmitter();
        var effect = ItemEffects.For(item.Category)!;
        var ctx = new ItemEffectContext
        {
            User = party.Lead,
            Item = item,
            Party = party,
            TargetPartySlot = slot,
            Emitter = emitter,
        };
        Assert.True(effect.CanApply(ctx));
        effect.Apply(ctx);
        return (party, emitter);
    }

    private static bool CanApplyRevive(Item item, Party? party, int? slot) =>
        ItemEffects
            .For(item.Category)!
            .CanApply(
                new ItemEffectContext
                {
                    User = party?.Lead ?? TestCreatures.Make(),
                    Item = item,
                    Party = party,
                    TargetPartySlot = slot,
                }
            );

    private static Item Revive50(int id = 62) =>
        Item(id, "revive", ItemCategory.Revive, revivePercent: 50);

    private static Item MaxRevive(int id = 63) =>
        Item(id, "max-revive", ItemCategory.Revive, revivePercent: 100);

    // Builds a party whose LEAD is a live starter and whose bench member is fainted (HP 0). The revive targets
    // the bench member at slot 1, so the active creature (slot 0 / lead) is deliberately untouched.
    private static Party PartyWithFaintedBench(int benchMaxHp = 200)
    {
        var party = new Party(TestCreatures.Make("Lead"));
        var bench = TestCreatures.Make("Bench", hp: benchMaxHp);
        bench.Attributes.ReceiveDamage(benchMaxHp); // faint it
        party.Add(bench);
        return party;
    }

    [Fact]
    public void Revive_RestoresFaintedMemberToHalfMaxHp()
    {
        var party = PartyWithFaintedBench(benchMaxHp: 200);
        var (p, em) = ApplyRevive(Revive50(), party, slot: 1);

        var bench = p.Members[1];
        Assert.True(bench.IsAlive());
        Assert.Equal(100, bench.Attributes.HP); // ⌈200 · ½⌉
        var ev = em.Of<Revived>().Single();
        Assert.Equal("Bench", ev.CreatureName);
        Assert.Equal(100, ev.HpAfter);
    }

    [Fact]
    public void Revive_FloorsTheHalfFraction_LikeGen1AndRecover()
    {
        // Gen 1 (and this repo's Recover) truncates the ½: 41 max HP → ⌊20.5⌋ = 20, NOT 21 (a ceil convention).
        // This MaxHP is chosen so floor and ceil genuinely disagree (a 200-HP pool wouldn't discriminate them).
        var party = new Party(TestCreatures.Make("Lead"));
        var bench = TestCreatures.Make("Bench", hp: 41);
        bench.Attributes.ReceiveDamage(41);
        party.Add(bench);

        ApplyRevive(Revive50(), party, slot: 1);
        Assert.Equal(20, party.Members[1].Attributes.HP); // ⌊41 · ½⌋
    }

    [Fact]
    public void Revive_NeverComesBackBelowOne_OnATinyPool()
    {
        // ⌊1 · ½⌋ = 0, but Math.Max(1, …) keeps a revived member ≥ 1 HP.
        var party = new Party(TestCreatures.Make("Lead"));
        var bench = TestCreatures.Make("Bench", hp: 1);
        bench.Attributes.ReceiveDamage(1);
        party.Add(bench);

        ApplyRevive(Revive50(), party, slot: 1);
        Assert.Equal(1, party.Members[1].Attributes.HP);
    }

    [Fact]
    public void Revive_BringsTheMemberBackStatusless()
    {
        // A member that fainted WHILE afflicted (the common way — burn/poison chip) comes back clean (Gen 1):
        // both the transient battle status and any carried status are cleared, and the emitted party snapshot
        // shows it statusless (not a revived-but-still-poisoned creature on the roster panel).
        var party = new Party(TestCreatures.Make("Lead"));
        var bench = TestCreatures.Make("Bench", hp: 200);
        bench.Battle.Status = StatusCondition.BadPoison;
        bench.Battle.ToxicCounter = 5;
        bench.CarriedStatus = new CarriedStatus(StatusCondition.BadPoison, 0);
        bench.Attributes.ReceiveDamage(200); // faint it while badly poisoned
        party.Add(bench);

        var (p, em) = ApplyRevive(Revive50(), party, slot: 1);

        Assert.Equal(StatusCondition.None, p.Members[1].Battle.Status);
        Assert.Equal(1, p.Members[1].Battle.ToxicCounter); // escalation reset to baseline
        Assert.Null(p.Members[1].CarriedStatus); // won't re-apply on a later send-in
        Assert.Equal(StatusCondition.None, em.Of<PartyUpdated>().Single().Members[1].Status);
    }

    [Fact]
    public void MaxRevive_RestoresFaintedMemberToFullHp()
    {
        var party = PartyWithFaintedBench(benchMaxHp: 200);
        var (p, _) = ApplyRevive(MaxRevive(), party, slot: 1);
        Assert.Equal(200, p.Members[1].Attributes.HP);
    }

    [Fact]
    public void Revive_LeavesTheRevivedMemberBenched()
    {
        var party = PartyWithFaintedBench();
        ApplyRevive(Revive50(), party, slot: 1);
        // Reviving does not switch it in — the lead is unchanged (a send-in is the separate forced-switch path).
        Assert.Equal(0, party.LeadIndex);
        Assert.Equal("Lead", party.Lead.Name);
    }

    [Fact]
    public void Revive_EmitsAPartySnapshotSoTheBenchBarRepaints()
    {
        var party = PartyWithFaintedBench();
        var (_, em) = ApplyRevive(Revive50(), party, slot: 1);
        var snapshot = em.Of<PartyUpdated>().Single();
        Assert.Equal(100, snapshot.Members[1].Hp); // the revived member's restored HP is on the wire
    }

    [Fact]
    public void Revive_OnANonFaintedMember_HasNoEffect()
    {
        var party = new Party(TestCreatures.Make("Lead"));
        party.Add(TestCreatures.Make("Bench")); // healthy
        Assert.False(CanApplyRevive(Revive50(), party, slot: 1));
    }

    [Fact]
    public void Revive_WithAnOutOfRangeSlot_HasNoEffect()
    {
        var party = PartyWithFaintedBench();
        Assert.False(CanApplyRevive(Revive50(), party, slot: 5));
        Assert.False(CanApplyRevive(Revive50(), party, slot: null));
    }

    [Fact]
    public void Revive_WithNoParty_HasNoEffect() =>
        Assert.False(CanApplyRevive(Revive50(), party: null, slot: 0));
}
