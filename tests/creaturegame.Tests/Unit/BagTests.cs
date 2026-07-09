using creaturegame.Items;

namespace creaturegame.Tests.Unit;

/// <summary>
/// The transient run <see cref="Bag"/> — item-id → quantity. Pins the Gen 1 per-slot ceiling
/// (<see cref="Bag.MaxPerSlot"/> = 99): <see cref="Bag.Add"/> clamps and <see cref="Bag.IsFull"/> reports the
/// cap, so the shop's repeatable buy (the first path that can approach it) can refuse rather than overfill.
/// </summary>
public class BagTests
{
    [Fact]
    public void Add_ClampsAtTheGen1NinetyNinePerSlotCeiling()
    {
        var bag = new Bag();
        bag.Add(1, 90);
        bag.Add(1, 20); // would be 110 → clamped to 99

        Assert.Equal(99, bag.Count(1));
        Assert.Equal(Bag.MaxPerSlot, bag.Count(1));
    }

    [Fact]
    public void Add_AtTheCeiling_IsANoOp()
    {
        var bag = new Bag();
        bag.Add(1, Bag.MaxPerSlot);
        Assert.True(bag.IsFull(1));

        bag.Add(1); // already at 99 → stays 99
        Assert.Equal(Bag.MaxPerSlot, bag.Count(1));
    }

    [Fact]
    public void IsFull_TrueOnlyAtTheCeiling()
    {
        var bag = new Bag();
        Assert.False(bag.IsFull(1)); // empty

        bag.Add(1, Bag.MaxPerSlot - 1);
        Assert.False(bag.IsFull(1)); // 98

        bag.Add(1);
        Assert.True(bag.IsFull(1)); // 99
    }

    [Fact]
    public void TheCeilingIsPerItemSlot_NotTheWholeBag()
    {
        var bag = new Bag();
        bag.Add(1, Bag.MaxPerSlot);
        bag.Add(2, 5);

        Assert.True(bag.IsFull(1));
        Assert.False(bag.IsFull(2)); // a different slot is unaffected
        Assert.Equal(5, bag.Count(2));
    }
}
