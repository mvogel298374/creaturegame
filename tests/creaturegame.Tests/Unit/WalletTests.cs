using creaturegame.Items;

namespace creaturegame.Tests.Unit;

/// <summary>
/// <see cref="Wallet"/>: transient per-run gold, mirroring <see cref="Bag"/>'s single-writer contract —
/// <see cref="Wallet.Credit"/> adds, <see cref="Wallet.TrySpend"/> only succeeds when affordable.
/// </summary>
public class WalletTests
{
    [Fact]
    public void StartsAtZero()
    {
        Assert.Equal(0, new Wallet().Balance);
    }

    [Fact]
    public void Credit_AddsToBalance()
    {
        var wallet = new Wallet();
        wallet.Credit(50);
        wallet.Credit(25);

        Assert.Equal(75, wallet.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Credit_NonPositiveAmount_IsANoOp(int amount)
    {
        var wallet = new Wallet();
        wallet.Credit(amount);

        Assert.Equal(0, wallet.Balance);
    }

    [Fact]
    public void TrySpend_WhenAffordable_DeductsAndReturnsTrue()
    {
        var wallet = new Wallet();
        wallet.Credit(100);

        Assert.True(wallet.TrySpend(40));
        Assert.Equal(60, wallet.Balance);
    }

    [Fact]
    public void TrySpend_WhenNotAffordable_LeavesBalanceUnchanged()
    {
        var wallet = new Wallet();
        wallet.Credit(10);

        Assert.False(wallet.TrySpend(11));
        Assert.Equal(10, wallet.Balance);
    }

    [Fact]
    public void TrySpend_ExactBalance_SucceedsAndZeroesOut()
    {
        var wallet = new Wallet();
        wallet.Credit(30);

        Assert.True(wallet.TrySpend(30));
        Assert.Equal(0, wallet.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TrySpend_NonPositiveAmount_ReturnsFalse(int amount)
    {
        var wallet = new Wallet();
        wallet.Credit(100);

        Assert.False(wallet.TrySpend(amount));
        Assert.Equal(100, wallet.Balance);
    }
}
