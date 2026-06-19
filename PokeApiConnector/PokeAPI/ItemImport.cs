using System.Text.Json;
using creaturegame.DB;
using creaturegame.Items;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector.PokeAPI;

/// <summary>
/// Imports the Gen 1 battle-usable items into items.db. Network + DB only — the Gen 1 roster and the
/// pure mapping live in <see cref="ItemMapper"/>. PokeAPI has no Gen 1 membership signal for items, so
/// we fetch the hand-curated <see cref="ItemMapper.Gen1BattleItemNames"/> directly by slug. Idempotent
/// (upsert by Id; resilient per record).
/// </summary>
public class ItemImport
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task ImportGen1BattleItemsAsync()
    {
        using var context = new ItemsDbContext();
        foreach (var name in ItemMapper.Gen1BattleItemNames)
            await FetchAndUpsertItem(name, context);
    }

    private static async Task FetchAndUpsertItem(string itemName, ItemsDbContext context)
    {
        string url = $"https://pokeapi.co/api/v2/item/{itemName}/";
        try
        {
            HttpResponseMessage response = await PokeApiHttp.Client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            PokeApiItem? pokeItem = JsonSerializer.Deserialize<PokeApiItem>(json, JsonOpts);
            if (pokeItem == null)
                return;

            Item item = ItemMapper.MapToItem(pokeItem);

            var existing = await context
                .Items.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == item.Id);
            if (existing == null)
            {
                context.Items.Add(item);
                Console.WriteLine(
                    $"Imported New Item: {item.Name} (ID: {item.Id}, {item.Category})"
                );
            }
            else
            {
                context.Items.Update(item);
                Console.WriteLine(
                    $"Updated Existing Item: {item.Name} (ID: {item.Id}, {item.Category})"
                );
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching item '{itemName}' from {url}: {ex.Message}");
        }
    }
}
