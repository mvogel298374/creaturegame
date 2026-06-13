namespace PokeApiConnector;

/// <summary>
/// One process-wide <see cref="HttpClient"/> for the whole import run. Each Fetch*/Download* method used to
/// new-up (and dispose) its own client — and <c>FetchMoveDataByUrl</c>/<c>FetchPokemonDataByUrl</c> did so
/// once per move/species, ~165× inside a loop. That's the socket-exhaustion antipattern: a disposed client
/// leaves its socket in TIME_WAIT, and under a tight loop that can run the machine out of ephemeral ports
/// (a transient <see cref="System.Net.Sockets.SocketException"/>). <see cref="HttpClient"/> is thread-safe
/// and built to be shared and long-lived, so one static instance serves every request. It carries the
/// raw.githubusercontent-friendly User-Agent the sprite/cry downloaders need, and is deliberately never
/// disposed — it lives for the lifetime of this one-shot tool.
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
