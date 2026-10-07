using Microsoft.Extensions.Options;

namespace Cable.Ocpp.Services;

/// <summary>
/// Shared hosting (SmarterASP) stops the app pool after roughly 10 minutes without an
/// HTTP request — and an open WebSocket does not count as a request. The idle test
/// proved it: every charger was dropped with "server shutting down" after 11 quiet
/// minutes. Hitting our own public /health through IIS every few minutes keeps the
/// pool alive from the inside, independent of the panel's scheduled task (which is
/// still worth having). Disabled when <c>Ocpp:SelfPingUrl</c> is empty — on a VPS the
/// pool is always-on and this is just noise.
/// </summary>
public sealed class KeepAliveSelfPing(IOptions<OcppOptions> options, IHttpClientFactory httpClientFactory, ILogger<KeepAliveSelfPing> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = options.Value.SelfPingUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        var interval = TimeSpan.FromSeconds(Math.Max(60, options.Value.SelfPingIntervalSeconds));
        log.LogInformation("Keep-alive self-ping every {Interval}s → {Url}", interval.TotalSeconds, url);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var client = httpClientFactory.CreateClient(nameof(KeepAliveSelfPing));
                client.Timeout = TimeSpan.FromSeconds(20);
                using var response = await client.GetAsync(url, stoppingToken);
                if (!response.IsSuccessStatusCode)
                    log.LogWarning("Keep-alive self-ping returned {Status}", (int)response.StatusCode);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Keep-alive self-ping failed");
            }
        }
    }
}
