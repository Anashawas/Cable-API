using System.Text.Json;
using Application.Common.Interfaces;
using Application.Ocpp.Queries;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp;

/// <summary>
/// Derives the "did the unit reach us yet" state from the raw log's system rows
/// (CONNECT / DISCONNECT / REFUSED) for a charger that has not booted. Once it has
/// booted the state is simply Booted and the live connection state takes over.
/// </summary>
public static class OcppOnboarding
{
    public const string Waiting = "Waiting";
    public const string Connected = "Connected";
    public const string Refused = "Refused";
    public const string Booted = "Booted";

    public static async Task<OcppOnboardingDto> ComputeAsync(IApplicationDbContext db, string chargePointId, DateTime? lastBootAt, DateTime registeredAt, CancellationToken ct)
    {
        if (lastBootAt is DateTime booted)
            return new OcppOnboardingDto(Booted, null, null, booted, registeredAt);

        var last = await db.OcppRawMessages.AsNoTracking()
            .Where(m => m.ChargePointId == chargePointId && m.Direction == "sys")
            .OrderByDescending(m => m.Id)
            .Select(m => new { m.Action, m.Payload, m.CreatedAt })
            .FirstOrDefaultAsync(ct);

        if (last is null)
            return new OcppOnboardingDto(Waiting, null, null, null, registeredAt);

        switch (last.Action)
        {
            case "REFUSED":
            {
                int? status = null; string? reason = null;
                try
                {
                    using var doc = JsonDocument.Parse(last.Payload ?? "{}");
                    if (doc.RootElement.TryGetProperty("status", out var st) && st.TryGetInt32(out var code)) status = code;
                    if (doc.RootElement.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String) reason = r.GetString();
                }
                catch (JsonException) { }
                return new OcppOnboardingDto(Refused, reason, status, last.CreatedAt, registeredAt);
            }
            case "CONNECT":
                return new OcppOnboardingDto(Connected, "socket accepted, waiting for BootNotification", null, last.CreatedAt, registeredAt);
            default: // DISCONNECT before any boot
                return new OcppOnboardingDto(Waiting, "connected once but dropped before sending BootNotification", null, last.CreatedAt, registeredAt);
        }
    }
}
