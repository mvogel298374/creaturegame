namespace PokeApiConnector;

public static class CryDownloader
{
    private const int FirstId = 1;
    private const int LastId = 151;

    // Legacy = original 8-bit Game Boy cries, perfect for Gen 1
    private const string LegacyUrlTemplate =
        "https://raw.githubusercontent.com/PokeAPI/cries/main/cries/pokemon/legacy/{0}.ogg";

    public static async Task DownloadAllAsync()
    {
        string root = FindSolutionRoot();
        string cryDir = Path.Combine(root, "creaturegame.Web", "wwwroot", "audio", "cries");
        Directory.CreateDirectory(cryDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CreatureGame-Importer/1.0");
        http.Timeout = TimeSpan.FromSeconds(30);

        int downloaded = 0,
            skipped = 0,
            failed = 0;

        for (int id = FirstId; id <= LastId; id++)
        {
            string destPath = Path.Combine(cryDir, $"{id}.ogg");

            if (File.Exists(destPath))
            {
                skipped++;
                continue;
            }

            try
            {
                await DownloadFile(http, string.Format(LegacyUrlTemplate, id), destPath);
                downloaded++;
                Console.Write($"\r  Cries: {downloaded + skipped}/{LastId} ({failed} errors)   ");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"\n  [{id, 3}] FAILED: {ex.Message}");
            }
        }

        Console.WriteLine(
            $"\r  Done — {downloaded} downloaded, {skipped} already present, {failed} failed.          "
        );
    }

    private static async Task DownloadFile(HttpClient http, string url, string destPath)
    {
        byte[] bytes = await http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(destPath, bytes);
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "creaturegame.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
