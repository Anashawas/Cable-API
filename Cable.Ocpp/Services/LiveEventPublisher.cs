using System.Net.Http.Json;
using System.Threading.Channels;
using Application.Ocpp.Live;
using Microsoft.Extensions.Options;

namespace Cable.Ocpp.Services;

/// <summary>
/// Tells the API "something changed at station X" the moment a charger reports it, so the web
/// updates in under a second instead of on its next poll. Fire-and-forget on a bounded channel:
/// a slow or unreachable API never delays a charger's reply, and a lost event only means the page
/// catches up on its fallback poll. Events are small hints; the pages refetch the real data.
/// </summary>
public sealed class LiveEventPublisher(IHttpClientFactory httpClients, IOptions<OcppOptions> options, ILogger<LiveEventPublisher> log)
    : BackgroundService
{
    public const string HttpClientName = "live-events";

    private readonly Channel<OcppLiveEvent> _queue = Channel.CreateBounded<OcppLiveEvent>(new BoundedChannelOptions(5_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private bool Enabled => !string.IsNullOrWhiteSpace(options.Value.ApiEventsUrl) && !string.IsNullOrWhiteSpace(options.Value.StatusApiKey);

    public void Publish(OcppLiveEvent e)
    {
        if (Enabled) _queue.Writer.TryWrite(e);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            log.LogInformation("Live events off (Ocpp:ApiEventsUrl or Ocpp:StatusApiKey empty) — the web relies on polling");
            return;
        }

        var batch = new List<OcppLiveEvent>(100);
        var failures = 0;
        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            batch.Clear();
            while (batch.Count < 100 && _queue.Reader.TryRead(out var e)) batch.Add(e);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.ApiEventsUrl)
                {
                    Content = JsonContent.Create(batch),
                };
                request.Headers.Add("X-Api-Key", options.Value.StatusApiKey);
                using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, stoppingToken);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
                if (failures > 0) log.LogInformation("Live events delivered again after {Failures} failure(s)", failures);
                failures = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // Dropped, not retried: the next change brings a fresh event and the pages poll as a fallback.
                if (failures++ % 20 == 0)
                    log.LogWarning(ex, "Live events: could not post {Count} event(s) to the API ({Failures} failure(s) so far)", batch.Count, failures);
                // Back off while the API is down (up to 30 s); the bounded queue drops the oldest hints meanwhile.
                if (failures > 2)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, failures)), stoppingToken);
            }
        }
    }
}
