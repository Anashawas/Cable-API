using System.Globalization;
using System.Text.Json;
using Cable.Ocpp.Transport;

namespace Cable.Ocpp.Handlers;

/// <summary>
/// One charger-initiated OCPP action. Registered as a keyed scoped service under the
/// action name, so each message runs in its own DI scope with its own DbContext.
/// Return the CALLRESULT payload; throw <see cref="Protocol.OcppCallErrorException"/>
/// to answer CALLERROR.
/// </summary>
public interface IOcppHandler
{
    Task<object> HandleAsync(OcppSession session, JsonElement payload, CancellationToken cancellationToken);
}

/// <summary>Tolerant readers for OCPP payloads — a missing or oddly typed field is null, never an exception.</summary>
public static class OcppPayload
{
    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p)
            ? p.ValueKind switch
            {
                JsonValueKind.String => p.GetString(),
                JsonValueKind.Number => p.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            }
            : null;

    public static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p)
            ? p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v
              : p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s
              : null
            : null;

    public static long? Long(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p)
            ? p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v) ? v
              : p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) ? (long)Math.Round(d)
              : p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s
              : null
            : null;

    /// <summary>OCPP timestamps are ISO-8601 UTC. A value without an offset is taken as UTC, not server-local.</summary>
    public static DateTime? Date(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            return null;
        if (!DateTime.TryParse(p.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d))
            return null;
        return DateTime.SpecifyKind(d, DateTimeKind.Utc);
    }

    public static JsonElement? Array(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array ? p : null;
}
