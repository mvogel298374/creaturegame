using creaturegame.DB;
using creaturegame.Items;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Integration;

/// <summary>Pins pokered's Gen 1 prices in the live <c>items.db</c> (DATA_IMPORT.md §4.5).
/// <para>Requires <c>items.db</c> populated — run <c>PokeApiConnector</c> on a fresh checkout.</para></summary>
public class ItemPriceDataContractTests
{
    private static List<Item> LoadItems()
    {
        using var ctx = new ItemsDbContext();
        var items = ctx.Items.AsNoTracking().ToList();
        if (items.Count == 0)
            throw new InvalidOperationException(
                "items.db has no items. Run `dotnet run --project PokeApiConnector`."
            );
        return items;
    }

    [Theory]
    [InlineData("poke-ball", 200)]
    [InlineData("great-ball", 600)]
    [InlineData("ultra-ball", 1200)]
    [InlineData("master-ball", 0)]
    [InlineData("safari-ball", 1000)]
    [InlineData("potion", 300)]
    [InlineData("super-potion", 700)]
    [InlineData("hyper-potion", 1500)]
    [InlineData("max-potion", 2500)]
    [InlineData("full-restore", 3000)]
    [InlineData("antidote", 100)]
    [InlineData("burn-heal", 250)]
    [InlineData("ice-heal", 250)]
    [InlineData("awakening", 200)]
    [InlineData("paralyze-heal", 200)]
    [InlineData("full-heal", 600)]
    [InlineData("revive", 1500)]
    [InlineData("ether", 0)]
    [InlineData("max-ether", 0)]
    [InlineData("elixir", 0)]
    [InlineData("max-elixir", 0)]
    [InlineData("x-attack", 500)]
    [InlineData("x-defense", 550)]
    [InlineData("x-speed", 350)]
    [InlineData("x-sp-atk", 350)]
    [InlineData("x-accuracy", 950)]
    [InlineData("dire-hit", 650)]
    [InlineData("guard-spec", 700)]
    public void StoredPrice_IsPokeredsGen1Price(string slug, int gen1Price)
    {
        Assert.Equal(gen1Price, LoadItems().Single(i => i.Name == slug).Cost);
    }

    [Fact]
    public void EveryStoredPrice_MatchesTheImportersGen1Table()
    {
        Assert.All(LoadItems(), item => Assert.Equal(ItemMapper.Gen1Prices[item.Name!], item.Cost));
    }
}
