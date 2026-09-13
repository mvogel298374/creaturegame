namespace PokeApiConnector;

/// <summary>One process-wide, deliberately never-disposed <see cref="HttpClient"/> for the whole import
/// run — avoids the per-request-`new HttpClient()` socket-exhaustion antipattern (DATA_IMPORT.md §6).
/// </summary>
internal static class PokeApiHttp
{
    public static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CreatureGame-Importer/1.0");
        return client;
    }
}
