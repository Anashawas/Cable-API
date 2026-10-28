using System.Globalization;
using Application.Ocpp.Commands.ChargeLimits;

namespace Application.Ocpp;

/// <summary>
/// The pushes a driver gets about their own charging session (app start, or a card linked to their account):
/// "charging started" and "charging finished" with the numbers. Pure text, so the wording is tested on its own.
/// Numbers use invariant digits in both languages, like the rest of the Cable Connect texts.
/// </summary>
public static class DriverSessionMessages
{
    public const string Started = "started";
    public const string Completed = "completed";

    /// <summary>FCM / inbox data type the app routes on (open the session screen with sessionId).</summary>
    public const string DataType = "ocpp_session";

    public sealed record Session(
        string? StationName,
        string? ChargerName,
        int ConnectorId,
        DateTime StartedAt,
        DateTime? StoppedAt,
        decimal? EnergyKwh,
        int? CostFils,
        int? SocStartPercent,
        int? SocEndPercent,
        string? StopReason,
        string? LimitKind,
        decimal? LimitValue,
        DateTime? LimitReachedAt);

    public static (string Title, string Body) Compose(string kind, Session s, string? language)
    {
        var ar = !string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        return kind == Completed ? Finished(s, ar) : Begun(s, ar);
    }

    private static (string, string) Begun(Session s, bool ar)
    {
        var where = Where(s, ar);
        var limit = s.LimitKind is null ? ""
            : ar ? " · يتوقف عند " + OcppChargeLimit.DescribeAr(s.LimitKind, s.LimitValue)
                 : " · stops at " + OcppChargeLimit.Describe(s.LimitKind, s.LimitValue);
        return ar
            ? ("بدأ الشحن ⚡", where + limit)
            : ("Charging started ⚡", where + limit);
    }

    private static (string, string) Finished(Session s, bool ar)
    {
        var station = string.IsNullOrWhiteSpace(s.StationName) ? null : s.StationName.Trim();
        var title = ar
            ? "انتهى الشحن ✔" + (station is null ? "" : " — " + station)
            : "Charging finished ✔" + (station is null ? "" : " — " + station);

        var parts = new List<string>();
        if (s.EnergyKwh is decimal kwh) parts.Add(kwh.ToString("0.00", CultureInfo.InvariantCulture) + " kWh");
        if (s.CostFils is int fils) parts.Add((fils / 1000m).ToString("0.000", CultureInfo.InvariantCulture) + (ar ? " د" : " JOD"));
        if (s.StoppedAt is DateTime stopped) parts.Add(Duration(stopped - s.StartedAt, ar));
        if (s.SocStartPercent is int from && s.SocEndPercent is int to && to >= from)
            parts.Add(ar ? $"البطارية {from}% ← {to}%" : $"battery {from}% → {to}%");

        var (en, arText) = ChargeLimitDto.StopText(s.StopReason, s.LimitKind, s.LimitValue, s.LimitReachedAt);
        var why = ar ? arText : en;
        var body = string.Join(" · ", parts);
        if (!string.IsNullOrWhiteSpace(why)) body += (body.Length > 0 ? "\n" : "") + (ar ? "السبب: " : "Reason: ") + why;
        return (title, body);
    }

    /// <summary>"Station · charger · plug 2" — the charger's driver-facing name only (never the internal id like RH4).</summary>
    private static string Where(Session s, bool ar)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.StationName)) parts.Add(s.StationName.Trim());
        if (!string.IsNullOrWhiteSpace(s.ChargerName)) parts.Add(s.ChargerName.Trim());
        if (s.ConnectorId > 0) parts.Add((ar ? "المقبس " : "plug ") + s.ConnectorId);
        return string.Join(" · ", parts);
    }

    /// <summary>"1 س 4 د" / "1 h 4 min"; under a minute → "1 د" / "1 min".</summary>
    public static string Duration(TimeSpan span, bool ar)
    {
        var minutes = Math.Max(1, (int)Math.Round(span.TotalMinutes));
        var h = minutes / 60;
        var m = minutes % 60;
        if (ar) return h > 0 ? (m > 0 ? $"{h} س {m} د" : $"{h} س") : $"{m} د";
        return h > 0 ? (m > 0 ? $"{h} h {m} min" : $"{h} h") : $"{m} min";
    }
}
