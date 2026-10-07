using System.Text.Json;
using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Cable.Ocpp.Services;

/// <summary>
/// A CALLRESULT only means "the unit received the request". The proof it took effect is a
/// later charger-initiated message: BootNotification after Reset, StatusNotification after
/// ChangeAvailability / UnlockConnector, the requested message after TriggerMessage. The
/// handlers call this to stamp CompletedAt on the most recent matching, still-open command
/// row — so the admin's history can tell "accepted" from "confirmed".
/// </summary>
public sealed class CommandConfirmer(IApplicationDbContext db, ILogger<CommandConfirmer> log)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public async Task ConfirmAsync(int ocppChargePointId, string action, Func<JsonElement, bool> requestMatches, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow - Window;
        var candidates = await db.OcppCommands
            .Where(c => c.OcppChargePointId == ocppChargePointId && c.Action == action
                        && c.Status == "Answered" && c.CompletedAt == null && !c.IsDeleted
                        && c.ResultStatus != "Rejected" && c.ResultStatus != "NotSupported" && c.ResultStatus != "UnlockFailed"
                        && c.CreatedAt >= since)
            .OrderByDescending(c => c.Id)
            .Take(5)
            .ToListAsync(cancellationToken);

        foreach (var cmd in candidates)
        {
            bool matches;
            try
            {
                using var doc = JsonDocument.Parse(cmd.RequestPayload);
                matches = requestMatches(doc.RootElement);
            }
            catch (JsonException) { continue; }
            if (!matches) continue;

            cmd.CompletedAt = DateTime.UtcNow;
            await db.SaveChanges(cancellationToken);
            log.LogInformation("Command {Id} ({Action}) confirmed by the charger {Seconds}s after it was sent",
                cmd.Id, action, (int)(cmd.CompletedAt.Value - cmd.CreatedAt).TotalSeconds);
            return;
        }
    }

    public static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : null;

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
