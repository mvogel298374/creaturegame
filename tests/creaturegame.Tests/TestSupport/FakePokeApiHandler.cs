using System.Net;
using System.Text.Json;

namespace creaturegame.Tests.TestSupport;

/// <summary>A canned PokeAPI: each registered url answers with its status and JSON body; any other url is 404.</summary>
public sealed class FakePokeApiHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _routes = new();

    public FakePokeApiHandler Json(string url, object body) =>
        RawJson(url, JsonSerializer.Serialize(body));

    public FakePokeApiHandler RawJson(string url, string json)
    {
        _routes[url] = (HttpStatusCode.OK, json);
        return this;
    }

    public FakePokeApiHandler Fail(
        string url,
        HttpStatusCode status = HttpStatusCode.InternalServerError
    )
    {
        _routes[url] = (status, "");
        return this;
    }

    public HttpClient Client() => new(this);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var (status, body) = _routes.TryGetValue(request.RequestUri!.ToString(), out var route)
            ? route
            : (HttpStatusCode.NotFound, "");
        return Task.FromResult(
            new HttpResponseMessage(status) { Content = new StringContent(body) }
        );
    }
}
