using System.Net;
using System.Net.Http.Headers;
using PokeApiConnector;

namespace creaturegame.Tests.Unit;

public class RetryHandlerTests
{
    private const string Url = "https://pokeapi.co/api/v2/move/1/";

    // Answers each request with the next scripted step: a status code, or an exception to throw.
    private sealed class ScriptedHandler(params object[] steps) : HttpMessageHandler
    {
        private readonly Queue<object> _steps = new(steps);
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return _steps.Dequeue() switch
            {
                Exception ex => Task.FromException<HttpResponseMessage>(ex),
                HttpResponseMessage response => Task.FromResult(response),
                HttpStatusCode status => Task.FromResult(new HttpResponseMessage(status)),
                var step => throw new InvalidOperationException($"bad step {step}"),
            };
        }
    }

    private static (HttpClient Client, List<TimeSpan> Waits) Client(ScriptedHandler inner)
    {
        var waits = new List<TimeSpan>();
        var handler = new RetryHandler(
            inner,
            (wait, _) =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            }
        );
        return (new HttpClient(handler), waits);
    }

    private static HttpResponseMessage TooManyRequests(TimeSpan retryAfter) =>
        new(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new RetryConditionHeaderValue(retryAfter) },
        };

    [Fact]
    public async Task TwoServiceUnavailables_ThenOk_Succeeds_WithTheBackoffWaits()
    {
        var inner = new ScriptedHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK
        );
        var (client, waits) = Client(inner);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, inner.Calls);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task ATooManyRequests_WaitsForItsRetryAfter()
    {
        var inner = new ScriptedHandler(
            TooManyRequests(TimeSpan.FromSeconds(7)),
            TooManyRequests(TimeSpan.FromSeconds(3)),
            HttpStatusCode.OK
        );
        var (client, waits) = Client(inner);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(3)], waits);
    }

    [Fact]
    public async Task ANetworkError_IsRetried()
    {
        var inner = new ScriptedHandler(new HttpRequestException("reset"), HttpStatusCode.OK);
        var (client, _) = Client(inner);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task FourFailures_GiveUp_AndReturnTheLastFailure()
    {
        var inner = new ScriptedHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable
        );
        var (client, waits) = Client(inner);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1 + RetryHandler.MaxRetries, inner.Calls);
        Assert.Equal(3, waits.Count);
    }

    [Fact]
    public async Task FourNetworkErrors_GiveUp_AndThrow()
    {
        var inner = new ScriptedHandler(
            new HttpRequestException("1"),
            new HttpRequestException("2"),
            new HttpRequestException("3"),
            new HttpRequestException("4")
        );
        var (client, _) = Client(inner);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Url));
        Assert.Equal(4, inner.Calls);
    }

    [Fact]
    public async Task ANotFound_IsNotRetried()
    {
        var inner = new ScriptedHandler(HttpStatusCode.NotFound);
        var (client, waits) = Client(inner);

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(waits);
    }
}
