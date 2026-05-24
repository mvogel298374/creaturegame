namespace PokeApiConnector;

public static class SpriteDownloader
{
    private const int FirstId = 1;
    private const int LastId  = 151;

    private const string FrontUrlTemplate = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{0}.png";
    private const string BackUrlTemplate  = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/back/{0}.png";

    public static async Task DownloadAllAsync()
    {
        string root     = FindSolutionRoot();
        string frontDir = Path.Combine(root, "creaturegame.Web", "wwwroot", "sprites", "front");
        string backDir  = Path.Combine(root, "creaturegame.Web", "wwwroot", "sprites", "back");
        Directory.CreateDirectory(frontDir);
        Directory.CreateDirectory(backDir);

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CreatureGame-Importer/1.0");
        http.Timeout = TimeSpan.FromSeconds(30);

        int downloaded = 0, skipped = 0, failed = 0;

        for (int id = FirstId; id <= LastId; id++)
        {
            string frontPath = Path.Combine(frontDir, $"{id}.png");
            string backPath  = Path.Combine(backDir,  $"{id}.png");

            if (File.Exists(frontPath) && File.Exists(backPath))
            {
                skipped++;
                continue;
            }

            try
            {
                if (!File.Exists(frontPath))
                    await DownloadFile(http, string.Format(FrontUrlTemplate, id), frontPath);

                if (!File.Exists(backPath))
                    await DownloadFile(http, string.Format(BackUrlTemplate, id), backPath);

                downloaded++;
                Console.Write($"\r  Sprites: {downloaded + skipped}/{LastId} ({failed} errors)   ");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"\n  [{id,3}] FAILED: {ex.Message}");
            }
        }

        Console.WriteLine($"\r  Done — {downloaded} downloaded, {skipped} already present, {failed} failed.          ");
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
