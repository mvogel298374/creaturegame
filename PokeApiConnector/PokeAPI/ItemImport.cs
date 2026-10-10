using System.Text.Json;
using creaturegame.DB;
using creaturegame.Items;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector.PokeAPI;

/// <summary>Imports the Gen 1 battle-usable items into items.db (DATA_IMPORT.md §4.5). Network + DB
/// only — the roster and mapping live in <see cref="ItemMapper"/>.</summary>
public class ItemImport
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Imports the Gen 1 battle-item roster; returns the urls that failed (empty when clean).</summary>
    public static async Task<IReadOnlyList<string>> ImportGen1BattleItemsAsync(
        HttpClient? http = null,
        Func<ItemsDbContext>? createContext = null
    )
    {
        http ??= PokeApiHttp.Client;
        var failed = new List<string>();
        using var context = (createContext ?? (() => new ItemsDbContext()))();
        foreach (var name in ItemMapper.Gen1BattleItemNames)
        {
            string url = $"https://pokeapi.co/api/v2/item/{name}/";
            if (!await FetchAndUpsertItem(url, name, context, http))
                failed.Add(url);
        }
        return failed;
    }

    private static async Task<bool> FetchAndUpsertItem(
        string url,
        string itemName,
        ItemsDbContext context,
        HttpClient http
    )
    {
        try
        {
            HttpResponseMessage response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            PokeApiItem? pokeItem = JsonSerializer.Deserialize<PokeApiItem>(json, JsonOpts);
            if (pokeItem == null)
            {
                Console.WriteLine($"No usable item data returned for '{itemName}'.");
                return false;
            }

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
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching item '{itemName}' from {url}: {ex.Message}");
            context.ChangeTracker.Clear(); // a failed save stays tracked and would fail every later one
            return false;
        }
    }
}
