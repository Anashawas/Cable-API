using System.Text.Json;
using Application.Common.Interfaces;
using Cable.Core.Utilities;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Pricing;

// ---------------------------------------------------------------------------
// Session price — what a Cable Connect session costs under the time-of-use tariff.
//
// The energy of a session is spread over the tariff windows it ran through: every
// stretch between two meter readings is split at the window boundaries (Asia/Amman)
// in proportion to time, so a session that starts in off-peak and ends in peak is
// billed at both rates. Prices are fils per kWh; the result is whole fils.
// ---------------------------------------------------------------------------

public record SessionPriceLineDto(string Key, string NameEn, string NameAr, decimal Kwh, int PriceFils, int Fils);

public record SessionPriceDto(int CostFils, int TariffVersion, List<SessionPriceLineDto> Lines)
{
    public decimal CostJod => CostFils / 1000m;
}

public static class SessionPricer
{
    /// <param name="samples">Energy register readings (UTC, Wh) during the session, any order; readings outside the session or going backwards are ignored.</param>
    public static SessionPriceDto Compute(TouTariffDto tariff, DateTime startUtc, DateTime stopUtc, long meterStartWh, long meterStopWh,
        IEnumerable<(DateTime AtUtc, long Wh)> samples)
    {
        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (stopUtc < startUtc) stopUtc = startUtc;

        // Monotonic path of (time, register) from start to stop.
        var points = new List<(DateTime At, long Wh)> { (startUtc, meterStartWh) };
        foreach (var s in samples.Where(s => s.AtUtc > startUtc && s.AtUtc < stopUtc && s.Wh >= meterStartWh && s.Wh <= meterStopWh).OrderBy(s => s.AtUtc))
            if (s.Wh >= points[^1].Wh && s.AtUtc >= points[^1].At) points.Add((s.AtUtc, s.Wh));
        points.Add((stopUtc, Math.Max(meterStopWh, points[^1].Wh)));

        for (var i = 1; i < points.Count; i++)
        {
            var (t0, w0) = points[i - 1];
            var (t1, w1) = points[i];
            var wh = w1 - w0;
            if (wh <= 0) continue;
            foreach (var (key, share) in SplitByWindow(tariff, t0, t1))
                totals[key] = totals.GetValueOrDefault(key) + wh * share / 1000m;
        }

        var lines = tariff.Windows
            .Where(w => totals.ContainsKey(w.Key))
            .Select(w =>
            {
                var kwh = Math.Round(totals[w.Key], 3, MidpointRounding.AwayFromZero);
                var fils = (int)Math.Round(totals[w.Key] * w.PriceFils, 0, MidpointRounding.AwayFromZero);
                return new SessionPriceLineDto(w.Key, w.NameEn, w.NameAr, kwh, w.PriceFils, fils);
            })
            .Where(l => l.Kwh > 0 || l.Fils > 0)
            .ToList();

        return new SessionPriceDto(lines.Sum(l => l.Fils), tariff.Version, lines);
    }

    /// <summary>The tariff window in force at a UTC instant (Jordan wall-clock), for "rate now" displays.</summary>
    public static TouWindowDto? WindowNow(TouTariffDto tariff, DateTime utc) => WindowAt(tariff, JordanTime.FromUtc(utc)).Window;

    /// <summary>Share of the span [t0, t1] (UTC) that falls in each tariff window, by Jordan wall-clock time.</summary>
    private static IEnumerable<(string Key, decimal Share)> SplitByWindow(TouTariffDto tariff, DateTime t0, DateTime t1)
    {
        var total = (t1 - t0).Ticks;
        if (total <= 0)
        {
            var w = WindowAt(tariff, JordanTime.FromUtc(t0));
            if (w.Window is not null) yield return (w.Window.Key, 1m);
            yield break;
        }

        var cur = t0;
        var guard = 0;
        while (cur < t1 && guard++ < 200)
        {
            var local = JordanTime.FromUtc(cur);
            var (window, endLocal) = WindowAt(tariff, local);
            if (window is null) yield break;
            var endUtc = JordanTime.ToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified));
            var next = endUtc <= cur ? t1 : endUtc < t1 ? endUtc : t1;
            yield return (window.Key, (decimal)(next - cur).Ticks / total);
            cur = next;
        }
    }

    /// <summary>The window containing a local wall-clock instant, and when (local) that window ends. Windows may cross midnight (EndMin > 1440).</summary>
    private static (TouWindowDto? Window, DateTime EndLocal) WindowAt(TouTariffDto tariff, DateTime local)
    {
        var day = local.Date;
        var m = (int)(local - day).TotalMinutes;
        foreach (var w in tariff.Windows)
        {
            if (w.StartMin <= m && m < w.EndMin) return (w, day.AddMinutes(w.EndMin));
            if (w.StartMin <= m + 1440 && m + 1440 < w.EndMin) return (w, day.AddMinutes(w.EndMin - 1440));
        }
        return (null, day.AddDays(1));
    }
}

/// <summary>Prices a closed OcppTransaction in place from the active tariff and its meter samples.</summary>
public static class SessionPricingService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <returns>true when CostFils was set; false when the session cannot be priced (open, orphan, unknown energy, no tariff).</returns>
    public static async Task<bool> PriceAsync(IApplicationDbContext db, OcppTransaction tx, TouTariffDto? tariff, CancellationToken ct)
    {
        if (tx.IsOpen || tx.IsOrphan || tx.StoppedAt is null || tx.MeterStopWh is null || tx.EnergyKwh is null) return false;
        tariff ??= await TouTariffLoader.LoadAsync(db, ct);
        if (tariff is null) return false;

        var samples = await db.OcppMeterValues.AsNoTracking()
            .Where(m => m.OcppTransactionId == tx.Id && m.EnergyWh != null)
            .OrderBy(m => m.MeasuredAt)
            .Select(m => new { m.MeasuredAt, m.EnergyWh })
            .ToListAsync(ct);

        var price = SessionPricer.Compute(tariff, tx.StartedAt, tx.StoppedAt.Value, tx.MeterStartWh, tx.MeterStopWh.Value,
            samples.Select(s => (s.MeasuredAt, s.EnergyWh!.Value)));

        tx.CostFils = price.CostFils;
        tx.TariffVersion = price.TariffVersion;
        tx.CostBreakdownJson = JsonSerializer.Serialize(price.Lines, Json);
        tx.PricedAt = DateTime.UtcNow;
        return true;
    }

    public static List<SessionPriceLineDto>? ReadLines(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<SessionPriceLineDto>>(json, Json); }
        catch (JsonException) { return null; }
    }
}
