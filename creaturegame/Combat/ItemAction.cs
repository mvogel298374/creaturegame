using creaturegame.Creatures;
using creaturegame.Items;

namespace creaturegame.Combat;

/// <summary>
/// A turn spent using a bag item on the user's own creature. The item analogue of
/// <see cref="AttackAction"/>: it resolves the effect via <see cref="ItemEffects"/>, and on a successful
/// use announces it (<see cref="ItemUsed"/>), runs the effect, and consumes one from the <see cref="Bag"/>.
/// <para>Gen 1: using an item takes the whole turn and resolves <b>before</b> either side's move — so its
/// <see cref="Priority"/> sits above any move's (moves cap at +1). Status (sleep/paralysis/confusion)
/// does not gate item use, so <see cref="Battle"/> runs this action without the <c>CanAct</c> check it
/// applies to attacks.</para>
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

    /// <summary>Above any move priority (Quick Attack is +1) so an item resolves first in Battle's queue.</summary>
    public const int ItemPriority = 6;

    public ItemAction(
        Creature source,
        Item item,
        int? targetMoveSlot,
        Bag bag,
        IBattleEventEmitter? emitter = null,
        Party? party = null,
        int? targetPartySlot = null
    )
    {
        Source = source;
        _item = item;
        _targetMoveSlot = targetMoveSlot;
        _targetPartySlot = targetPartySlot;
        _party = party;
        _bag = bag;
        _emitter = emitter;
        Priority = ItemPriority;
    }

    public Task ExecuteAsync()
    {
        var effect = ItemEffects.For(_item.Category);
        var ctx = new ItemEffectContext
        {
            User = Source,
            Item = _item,
            TargetMoveSlot = _targetMoveSlot,
            Party = _party,
            TargetPartySlot = _targetPartySlot,
            Emitter = _emitter,
        };

        // Refuse a use that wouldn't do anything (no effect for the category, or the precondition isn't
        // met) or that isn't actually in the bag — announce nothing, consume nothing (Gen 1 menu rule).
        if (effect is null || !_bag.Has(_item.Id) || !effect.CanApply(ctx))
        {
            _emitter?.Emit(new ItemUseFailed(_item.Name ?? ""));
            return Task.CompletedTask;
        }

        _emitter?.Emit(new ItemUsed(_item.Name ?? "", AnnounceTargetName()));
        effect.Apply(ctx);
        _bag.Consume(_item.Id);
        return Task.CompletedTask;
    }

    // Who "Used X on …" names: a party-targeting item (Revive) acts on the benched member, not the active
    // creature, so name that member; every self-targeting item names Source. CanApply has already validated the
    // slot when we get here, so the range check is just belt-and-suspenders.
    private string AnnounceTargetName() =>
        _party is { } party && _targetPartySlot is { } slot && slot >= 0 && slot < party.Count
            ? party.Members[slot].Name
            : Source.Name;
}
