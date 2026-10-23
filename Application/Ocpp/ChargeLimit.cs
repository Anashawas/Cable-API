using System.Globalization;
using Cable.Core;

namespace Application.Ocpp;

/// <summary>
/// Charge limit: stop a session automatically at an amount (JOD), an energy (kWh) or a battery %.
/// OCPP 1.6 has no such limit on the unit, so Cable.Ocpp checks every MeterValues against it and
/// sends RemoteStopTransaction itself. It looks one reading ahead (power × sample interval) so the
/// session stops at the limit rather than one reading past it.
/// </summary>
public static class OcppChargeLimit
{
    public const string Jod = "Jod";
    public const string Kwh = "Kwh";
    public const string Soc = "Soc";

    public static readonly IReadOnlyList<string> Kinds = [Jod, Kwh, Soc];

    /// <summary>The unit's MeterValueSampleInterval at the pilot station; used when two readings are not yet known.</summary>
    public const int DefaultSampleSeconds = 15;

    /// <summary>Accepted ranges: at least something worth a session, at most more than any car takes.</summary>
    public static (decimal Min, decimal Max) Range(string kind) => kind switch
    {
        Jod => (0.5m, 200m),
        Kwh => (1m, 300m),
        Soc => (10m, 100m),
        _ => throw UnknownKind(kind),
    };

    /// <summary>Normalises the kind (case-insensitive) and checks the value; throws a 400 with the reason.</summary>
    public static (string Kind, decimal Value) Validate(string? kind, decimal? value)
    {
        var k = Kinds.FirstOrDefault(x => string.Equals(x, kind?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw UnknownKind(kind);
        if (value is not decimal v)
            throw new DataValidationException("ChargeLimitValue", IsArabic
                ? "أدخل قيمة الحد المطلوب."
                : "A value is required with a charge limit.");
        var (min, max) = Range(k);
        if (v < min || v > max)
            throw new DataValidationException("ChargeLimitValue", IsArabic
                ? $"يجب أن يكون الحد بين {DescribeAr(k, min)} و {DescribeAr(k, max)}."
                : $"A {k} limit must be between {Describe(k, min)} and {Describe(k, max)}.");
        return (k, k == Soc ? Math.Round(v) : Math.Round(v, 3));
    }

    // Invariant digits and "." in both languages: the API's default culture is Arabic, which would print "0٫5".
    public static string Describe(string? kind, decimal? value) => kind switch
    {
        Jod => $"{Num(value)} JOD",
        Kwh => $"{Num(value)} kWh",
        Soc => $"{Num(value)}%",
        _ => "",
    };

    public static string DescribeAr(string? kind, decimal? value) => kind switch
    {
        Jod => $"{Num(value)} دينار",
        Kwh => $"{Num(value)} كيلوواط ساعة",
        Soc => $"{Num(value)}% بطارية",
        _ => "",
    };

    private static string Num(decimal? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";

    private static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    private static DataValidationException UnknownKind(string? kind) => new("ChargeLimitKind", IsArabic
        ? $"نوع الحد '{kind}' غير معروف. الأنواع: {string.Join(", ", Kinds)}."
        : $"Unknown limit kind '{kind}'. Use {string.Join(", ", Kinds)}.");

    /// <summary>
    /// True when the session should be stopped now: the limit is reached, or will be before the next reading
    /// arrives at the current power. <paramref name="costFils"/> is the price so far at the tariff in force.
    /// </summary>
    public static bool ShouldStop(string kind, decimal limit, decimal energyKwh, int costFils, int? socPercent,
        int? powerW, int sampleSeconds, int currentRateFils)
    {
        var nextKwh = powerW is > 0 ? powerW.Value / 1000m * Math.Max(sampleSeconds, 1) / 3600m : 0m;
        return kind switch
        {
            Jod => costFils + nextKwh * currentRateFils >= limit * 1000m,
            Kwh => energyKwh + nextKwh >= limit,
            Soc => socPercent is int soc && soc >= limit,   // battery % is not linear in kWh: stop on the reading itself
            _ => false,
        };
    }
}
