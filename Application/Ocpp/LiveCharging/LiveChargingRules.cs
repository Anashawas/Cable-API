using System.Globalization;
using System.Text.RegularExpressions;
using Cable.Core.Constants;
using Cable.Core.Utilities;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// Sprint 5 live charging (A) — the pure rules: proximity, months, card tags and
// the texts the driver sees. No database here, so every rule is testable alone.
// ---------------------------------------------------------------------------

/// <summary>Great-circle distance (haversine) — accurate to well under a metre at station scale.</summary>
public static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_000d;

    public static double Meters(double lat1, double lng1, double lat2, double lng2)
    {
        static double Rad(double deg) => deg * Math.PI / 180d;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    /// <summary>A station row with 0 / 0 (never geocoded) or out-of-range values has no usable position.</summary>
    public static bool HasCoordinates(double lat, double lng) =>
        !(Math.Abs(lat) < 1e-6 && Math.Abs(lng) < 1e-6) && lat is >= -90 and <= 90 && lng is >= -180 and <= 180;
}

/// <summary>
/// 4.2: the driver must be at the station to start from the app — otherwise anyone could start a session on a
/// stranger's plugged-in car from home. The phone's own accuracy is given the benefit of the doubt, capped at
/// <see cref="MaxAccuracyCreditMeters"/> so a vague fix cannot stretch the radius indefinitely.
/// </summary>
public static class ProximityRule
{
    public const int MaxAccuracyCreditMeters = 100;

    public const string LocationRequiredCode = "LOCATION_REQUIRED";
    public const string TooFarCode = "TOO_FAR_FROM_STATION";

    public enum Outcome { Allowed, StationHasNoPosition, LocationMissing, TooFar }

    /// <param name="DistanceMeters">Rounded distance from the phone to the station; null when either position is unknown.</param>
    public sealed record Result(Outcome Outcome, int? DistanceMeters, int MaxMeters)
    {
        public bool IsAllowed => Outcome is Outcome.Allowed or Outcome.StationHasNoPosition;
    }

    /// <summary>
    /// Reject when distance − min(accuracy, 100) &gt; radius. A station without coordinates is always allowed;
    /// a missing phone position is refused only when <paramref name="requireLocation"/> (Ocpp.AppStart.RequireLocation).
    /// </summary>
    public static Result Evaluate(double stationLat, double stationLng, double? lat, double? lng, double? accuracyMeters,
        int maxMeters, bool requireLocation)
    {
        if (lat is null || lng is null)
        {
            if (!GeoDistance.HasCoordinates(stationLat, stationLng)) return new Result(Outcome.StationHasNoPosition, null, maxMeters);
            return new Result(requireLocation ? Outcome.LocationMissing : Outcome.Allowed, null, maxMeters);
        }

        if (!GeoDistance.HasCoordinates(stationLat, stationLng))
            return new Result(Outcome.StationHasNoPosition, null, maxMeters);

        var distance = GeoDistance.Meters(stationLat, stationLng, lat.Value, lng.Value);
        var credit = Math.Min(Math.Max(accuracyMeters ?? 0, 0), MaxAccuracyCreditMeters);
        var rounded = (int)Math.Round(distance, MidpointRounding.AwayFromZero);
        return distance - credit > maxMeters
            ? new Result(Outcome.TooFar, rounded, maxMeters)
            : new Result(Outcome.Allowed, rounded, maxMeters);
    }
}

/// <summary>4.8: "YYYY-MM" → the Jordan calendar month as a UTC range.</summary>
public static class JordanMonth
{
    public static bool TryParse(string? month, out int year, out int monthNumber)
    {
        year = 0; monthNumber = 0;
        if (string.IsNullOrWhiteSpace(month)) return false;
        if (!DateTime.TryParseExact(month.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
        year = d.Year; monthNumber = d.Month;
        return true;
    }

    /// <summary>[from, to) in UTC for the month, by Jordan wall clock.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) Range(int year, int month)
    {
        var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (JordanTime.ToUtc(start), JordanTime.ToUtc(start.AddMonths(1)));
    }

    public static (int Year, int Month) Current(DateTime utcNow)
    {
        var local = JordanTime.FromUtc(utcNow);
        return (local.Year, local.Month);
    }

    public static string Format(int year, int month) => $"{year:D4}-{month:D2}";
}

/// <summary>4.9: RFID cards a driver links to their account (OcppUserIdTag).</summary>
public static partial class DriverCardRules
{
    public const int MaxCardsPerUser = 5;

    public const string AlreadyLinkedCode = "CARD_ALREADY_LINKED";
    public const string LimitReachedCode = "CARD_LIMIT_REACHED";

    /// <summary>OCPP 1.6 idTag is a CiString20; printable, no spaces inside.</summary>
    [GeneratedRegex("^[A-Z0-9][A-Z0-9_.:#-]{0,19}$")]
    private static partial Regex TagPattern();

    public static string Normalize(string idTag) => OcppCredentials.NormalizeIdTag(idTag);

    public static bool IsValidFormat(string normalized) => TagPattern().IsMatch(normalized);

    /// <summary>Cable's own virtual tags (CBL-U… app, CBL-S… station) are never linkable by hand.</summary>
    public static bool IsReserved(string normalized) =>
        normalized.StartsWith(OcppVirtualTag.UserPrefix, StringComparison.OrdinalIgnoreCase)
        || OcppVirtualTag.IsStationTag(normalized);

    /// <summary>The app's own tag (CBL-U{id}) — hidden from the "my cards" list and not counted against the limit.</summary>
    public static bool IsAppTag(string idTag) => OcppVirtualTag.UserIdOf(OcppCredentials.NormalizeIdTag(idTag)) is not null;
}

/// <summary>Every driver-facing text of this feature, Arabic and English. Arabic is the default.</summary>
public static class LiveChargingTexts
{
    public static string LocationRequired() => Lang.Pick(
        "شغّل الموقع عشان نتأكد إنك عند المحطة، وبعدين ابدأ الشحن.",
        "Turn on location so we can confirm you are at the station, then start charging.");

    public static string TooFar(int distanceMeters, int maxMeters) => Lang.Pick(
        $"لازم تكون عند المحطة لتبدأ الشحن (أنت على بعد {distanceMeters} م، والحد {maxMeters} م).",
        $"You need to be at the station to start charging (you are {distanceMeters} m away; the limit is {maxMeters} m).");

    public static string CarNotFound() => Lang.Pick(
        "هاي السيارة مش موجودة بسياراتك.",
        "This car is not one of your cars.");

    public static string CardInvalid() => Lang.Pick(
        "رقم البطاقة غير صالح. اكتبه مثل ما هو مطبوع على البطاقة (حتى 20 خانة، أحرف وأرقام).",
        "This card number is not valid. Type it as printed on the card (up to 20 letters and digits).");

    public static string CardReserved() => Lang.Pick(
        "هذا الرقم محجوز لكيبل وما بينربط ببطاقة.",
        "This number is reserved by Cable and cannot be linked as a card.");

    public static string CardAlreadyLinked() => Lang.Pick(
        "هاي البطاقة مربوطة بحساب ثاني.",
        "This card is already linked to another account.");

    public static string CardLimitReached(int max) => Lang.Pick(
        $"وصلت للحد الأقصى ({max} بطاقات). احذف بطاقة قبل ما تضيف وحدة جديدة.",
        $"You have reached the limit of {max} cards. Remove one before adding another.");

    public static string CardNotFound() => Lang.Pick("ما لقينا هاي البطاقة.", "Card not found.");

    public static string MonthInvalid() => Lang.Pick(
        "الشهر لازم يكون بالشكل YYYY-MM، مثل 2026-10.",
        "month must look like YYYY-MM, e.g. 2026-10.");

    /// <summary>N-5: the driver's push when the car is done but still on the plug (push language, not the request's).</summary>
    public static (string Title, string Body) Parked(string? language, string? stationName, string? chargerName, int connectorId)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(stationName)) where.Add(stationName.Trim());
        if (!string.IsNullOrWhiteSpace(chargerName)) where.Add(chargerName.Trim());
        if (connectorId > 0) where.Add(Lang.Pick(language, "المقبس ", "plug ") + connectorId);
        var suffix = where.Count == 0 ? "" : " · " + string.Join(" · ", where);
        return (
            Lang.Pick(language, "سيارتك خلصت شحن — ممكن تفصلها؟", "Your car has finished charging — could you unplug it?"),
            Lang.Pick(language, "خلّي المقبس فاضي للسائق التالي", "Free the plug for the next driver") + suffix);
    }

    /// <summary>"+330 نقطة" / "+330 points" for the finished push.</summary>
    public static string Points(int points, bool arabic) =>
        arabic ? $"+{points} نقطة" : $"+{points} point{(points == 1 ? "" : "s")}";
}

/// <summary>N-5 / FCM data: the parked status the app routes on (type ocpp_session).</summary>
public static class DriverSessionStatus
{
    public const string Parked = "parked";
}
