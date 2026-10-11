using System.Net;

namespace PokeApiConnector;

/// <summary>Retries a request on 429, 5xx and network errors — see DATA_IMPORT.md §5.6 "Retries".</summary>
public sealed class RetryHandler : DelegatingHandler
{
    public const int MaxRetries = 3;
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public RetryHandler(
        HttpMessageHandler inner,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
        : base(inner) => _delay = delay ?? Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        for (int attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == MaxRetries)
                    return response;
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && attempt < MaxRetries)
            { }

            var wait = RetryAfter(response) ?? Backoff[attempt];
            response?.Dispose();
            await _delay(wait, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    // A timeout surfaces as TaskCanceledException without the caller having cancelled.
    private static bool IsTransient(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static TimeSpan? RetryAfter(HttpResponseMessage? response)
    {
        var header = response?.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta;
        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }
}
