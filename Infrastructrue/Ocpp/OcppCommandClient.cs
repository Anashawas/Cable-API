using System.Net.Http.Json;
using System.Text.Json;
using Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Ocpp;

/// <summary>Where the Cable.Ocpp host lives, for server-initiated commands.</summary>
public class OcppServerOptions
{
    public const string ConfigName = "OcppServer";

    /// <summary>Base URL of Cable.Ocpp, e.g. https://ocpp.cable-app.com (no trailing path).</summary>
    public string Url { get; set; } = "";

    /// <summary>Same value as Cable.Ocpp's Ocpp:StatusApiKey.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>HTTP timeout; must exceed the charger reply timeout (30 s default on the OCPP side).</summary>
    public int HttpTimeoutSeconds { get; set; } = 45;
}

/// <summary>POST {Url}/commands/{chargePointId} with the shared key; never throws for a charger-side failure.</summary>
public sealed class OcppCommandClient(IHttpClientFactory httpClientFactory, IOptions<OcppServerOptions> options, ILogger<OcppCommandClient> log)
    : IOcppCommandClient
{
    public const string HttpClientName = "CableOcpp";

    public string? ServerUrl => string.IsNullOrWhiteSpace(options.Value.Url) ? null : options.Value.Url;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<OcppCommandOutcome> SendAsync(string chargePointId, string action, object payload, CancellationToken cancellationToken, int? timeoutSeconds = null)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Url) || string.IsNullOrWhiteSpace(opts.ApiKey))
            return new OcppCommandOutcome("Unreachable", null, null, "OcppServer:Url / ApiKey is not configured on the API", 0);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(opts.HttpTimeoutSeconds, (timeoutSeconds ?? 30) + 10));

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.Url.TrimEnd('/')}/commands/{Uri.EscapeDataString(chargePointId)}");
            request.Headers.Add("X-Api-Key", opts.ApiKey);
            request.Content = JsonContent.Create(new { action, payload, timeoutSeconds }, options: Json);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                log.LogWarning("Cable.Ocpp refused {Action} for {ChargePointId}: {Status} {Body}", action, chargePointId, (int)response.StatusCode, body);
                return new OcppCommandOutcome("Unreachable", null, ((int)response.StatusCode).ToString(), $"OCPP server answered {(int)response.StatusCode}", 0);
            }

            var outcome = await response.Content.ReadFromJsonAsync<OcppCommandOutcome>(Json, cancellationToken);
            return outcome ?? new OcppCommandOutcome("Unreachable", null, null, "empty reply from the OCPP server", 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            log.LogWarning(ex, "Cable.Ocpp unreachable for {Action} / {ChargePointId}", action, chargePointId);
            return new OcppCommandOutcome("Unreachable", null, null, $"OCPP server unreachable: {ex.Message}", 0);
        }
    }
}
