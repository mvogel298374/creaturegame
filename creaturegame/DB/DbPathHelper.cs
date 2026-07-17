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

            // Deployed layout: the .db files are published next to the app binary (see
            // creaturegame.Web.csproj), and a real host has no solution file to find.
            if (File.Exists(Path.Combine(baseDir, dbName)))
            {
                _rootPath = baseDir;
            }
            else
            {
                // Dev/repo layout: walk up to the solution directory (repo root holds the DBs).
                DirectoryInfo? dir = new DirectoryInfo(baseDir);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "creaturegame.sln")))
                {
                    dir = dir.Parent;
                }

                _rootPath = dir?.FullName ?? baseDir;
            }
        }

        return Path.Combine(_rootPath, dbName);
    }
}
