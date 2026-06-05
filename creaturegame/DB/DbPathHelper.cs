using System;
using System.IO;

namespace creaturegame.DB;

public static class DbPathHelper
{
    private static string? _rootPath;

    public static string GetDatabasePath(string dbName)
    {
        if (_rootPath == null)
        {
            string baseDir = AppContext.BaseDirectory;
            DirectoryInfo? dir = new DirectoryInfo(baseDir);

            // Search upwards for the solution directory as a heuristic for the project root.
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "creaturegame.sln")))
            {
                dir = dir.Parent;
            }

            _rootPath = dir?.FullName ?? baseDir;
        }

        return Path.Combine(_rootPath, dbName);
    }
}
