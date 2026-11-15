using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// A turn spent using a bag item on a member of the user's own party — the active creature by default, or
/// another party member via <c>targetPartySlot</c>. The item analogue of
/// <see cref="AttackAction"/>: it resolves the effect via <see cref="ItemEffects"/>, and on a successful
/// use announces it (<see cref="ItemUsed"/>), runs the effect, and consumes one from the <see cref="Bag"/>.
/// <para>Using an item takes the whole turn; status does not gate it, so <see cref="Battle"/> skips <c>CanAct</c>.</para>
/// </summary>
public sealed class ItemAction : IBattleAction
{
    public Creature Source { get; }
    public int Priority { get; }

    private readonly Item _item;
    private readonly int? _targetMoveSlot;
    private readonly int? _targetPartySlot;
    private readonly Party? _party;
    private readonly Bag _bag;
    private readonly IBattleEventEmitter? _emitter;
    private readonly IBattleRules _rules;

    /// <summary>Above any move priority (Quick Attack is +1) so a queued item resolves first in Battle's queue.</summary>
    public const int ItemPriority = 6;

    public ItemAction(
        Creature source,
        Item item,
        int? targetMoveSlot,
        Bag bag,
        IBattleRules rules,
        IBattleEventEmitter? emitter = null,
        Party? party = null,
        int? targetPartySlot = null
    )
    {
        Source = source;
        _rules = rules;
        _item = item;
        _targetMoveSlot = targetMoveSlot;
        _targetPartySlot = targetPartySlot;
        _party = party;
        _bag = bag;
        _emitter = emitter;
        Priority = ItemPriority;
    }

    public string ItemName => _item.Name ?? "";

    /// <summary>False for a use that wouldn't do anything (no effect for the category, the precondition isn't met)
    /// or an item not in the bag.</summary>
    public bool CanUse()
    {
        var effect = ItemEffects.For(_item.Category);
        return effect is not null && _bag.Has(_item.Id) && effect.CanApply(NewContext());
    }

    private ItemEffectContext NewContext() =>
        new()
        {
            User = Source,
            Item = _item,
            TargetMoveSlot = _targetMoveSlot,
            Party = _party,
            TargetPartySlot = _targetPartySlot,
            Emitter = _emitter,
            Rules = _rules,
        };

    public Task ExecuteAsync()
    {
        // A refused use announces nothing and consumes nothing.
        if (!CanUse())
        {
            _emitter?.Emit(new ItemUseFailed(ItemName));
            return Task.CompletedTask;
        }

        var effect = ItemEffects.For(_item.Category)!;
        var ctx = NewContext();
        _emitter?.Emit(new ItemUsed(ItemName, ctx.ResolvedTarget.Name, ctx.ResolvedTarget.Id));
        effect.Apply(ctx);
        _bag.Consume(_item.Id);

        // Re-sync the roster panel whenever this use touched a BENCHED member (any category — Healing,
        // StatusCure, PpRestore, Revive; BattleStatBoost never resolves to a non-Source target so never
        // triggers this). The log narrates via ItemUsed + the effect's own event, but a benched member isn't
        // a nameplate, so its HP/status bar only ever updates off a party snapshot (the same vehicle
        // RecoveryRunEvent uses for the whole-party heal). The active creature's own nameplate already
        // reflects its state without a snapshot, so skip it when nothing benched changed.
        if (_party is { } party && !ReferenceEquals(ctx.ResolvedTarget, Source))
            _emitter?.Emit(new PartyUpdated(PartyProjection.Snapshot(party)));

        return Task.CompletedTask;
    }
}
