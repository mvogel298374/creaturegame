using creaturegame.DB;
using Microsoft.EntityFrameworkCore;

namespace PokeApiConnector;

/// <summary>Downloads battle-item sprites into <c>wwwroot/sprites/items/{id}.png</c> (DATA_IMPORT.md §4.4).
/// </summary>
public static class ItemSpriteDownloader
{
    // Returns the number of item sprites that failed to download.
    public static async Task<int> DownloadAllAsync()
    {
        string root = FindSolutionRoot();
        string itemsDir = Path.Combine(root, "creaturegame.Web", "wwwroot", "sprites", "items");
        Directory.CreateDirectory(itemsDir);

        await using var ctx = new ItemsDbContext();
        var items = await ctx
            .Items.AsNoTracking()
            .Where(i => i.SpriteUrl != null)
            .Select(i => new { i.Id, i.SpriteUrl })
            .ToListAsync();

        var http = PokeApiHttp.Client;
        int downloaded = 0,
            skipped = 0,
            failed = 0;

        foreach (var item in items)
        {
            string destPath = Path.Combine(itemsDir, $"{item.Id}.png");
            if (File.Exists(destPath))
            {
                skipped++;
                continue;
            }

            try
            {
                byte[] bytes = await http.GetByteArrayAsync(item.SpriteUrl!);
                await File.WriteAllBytesAsync(destPath, bytes);
                downloaded++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"\n  [item {item.Id}] FAILED: {ex.Message}");
            }
        }

        Console.WriteLine(
            $"  Item sprites — {downloaded} downloaded, {skipped} already present, {failed} failed."
        );
        return failed;
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "creaturegame.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
