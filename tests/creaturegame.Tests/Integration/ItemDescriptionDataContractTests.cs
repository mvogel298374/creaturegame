using creaturegame.DB;
using Microsoft.EntityFrameworkCore;
using PokeApiConnector.PokeAPI;

namespace creaturegame.Tests.Integration;

/// <summary>Pins the Gen 1 item descriptions in the live <c>items.db</c> (DATA_IMPORT.md §4.5).</summary>
public class ItemDescriptionDataContractTests
{
    [Fact]
    public void EveryStoredDescription_MatchesTheImportersGen1Table()
    {
        using var ctx = new ItemsDbContext();
        var items = ctx.Items.AsNoTracking().ToList();

        Assert.NotEmpty(items);
        Assert.All(
            items,
            item => Assert.Equal(ItemMapper.Gen1Descriptions[item.Name!], item.Description)
        );
    }
}
