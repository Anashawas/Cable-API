namespace Cable.Core.Constants;

// ---------------------------------------------------------------------------
// Sprint 5 — live charging (B): stickers, start method, session issues, plug reports.
// ---------------------------------------------------------------------------

/// <summary>
/// How a session was started, for the partner / admin lists (§11.d). Finer than <see cref="OcppStartSource"/>:
/// an app start made by scanning the plug's CHG- sticker is "Sticker". Rows written before the column read as their StartSource.
/// </summary>
public static class OcppStartMethod
{
    public const string App = "App";
    public const string Sticker = "Sticker";
    public const string Card = "Card";
    public const string Operator = "Operator";

    public static readonly string[] All = [App, Sticker, Card, Operator];

    /// <summary>Case-insensitive parse of a filter value; null when unknown.</summary>
    public static string? Parse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : All.FirstOrDefault(m => string.Equals(m, value.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Why a driver says the plug did not work (§13.3).</summary>
public static class OcppConnectorReportReason
{
    public const string NoResponse = "NoResponse";
    public const string Rejected = "Rejected";
    public const string ScreenError = "ScreenError";
    public const string Damaged = "Damaged";
    public const string Other = "Other";

    public static readonly string[] All = [NoResponse, Rejected, ScreenError, Damaged, Other];

    public static string? Parse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : All.FirstOrDefault(m => string.Equals(m, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Describe(string reason) => reason switch
    {
        NoResponse => "The charger did not respond",
        Rejected => "The charger refused to start",
        ScreenError => "Error on the charger's screen",
        Damaged => "The plug looks damaged",
        _ => "Other",
    };

    public static string DescribeAr(string reason) => reason switch
    {
        NoResponse => "الشاحن ما ردّ",
        Rejected => "الشاحن رفض البدء",
        ScreenError => "خطأ على شاشة الشاحن",
        Damaged => "المقبس خربان ظاهرياً",
        _ => "غير ذلك",
    };
}

/// <summary>"Problem with this session" reasons (§13.1).</summary>
public static class SessionIssueReason
{
    public const string WrongCost = "WrongCost";
    public const string StoppedEarly = "StoppedEarly";
    public const string NoEnergy = "NoEnergy";
    public const string CableStuck = "CableStuck";
    public const string PlugBroken = "PlugBroken";
    public const string Other = "Other";

    public static readonly string[] All = [WrongCost, StoppedEarly, NoEnergy, CableStuck, PlugBroken, Other];

    public static string? Parse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : All.FirstOrDefault(m => string.Equals(m, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Describe(string? reason) => reason switch
    {
        WrongCost => "Wrong cost",
        StoppedEarly => "Stopped before it finished",
        NoEnergy => "No energy (0 kWh)",
        CableStuck => "Cable would not unlock",
        PlugBroken => "Plug broken",
        _ => "Other",
    };

    public static string DescribeAr(string? reason) => reason switch
    {
        WrongCost => "الكلفة غلط",
        StoppedEarly => "وقف لحاله قبل ما يخلص",
        NoEnergy => "ما شحن (الطاقة صفر)",
        CableStuck => "الكبل ما انفصل",
        PlugBroken => "المقبس خربان",
        _ => "غير ذلك",
    };
}

/// <summary>UserComplaint.Kind.</summary>
public static class UserComplaintKind
{
    public const string General = "General";
    public const string ChargingSession = "ChargingSession";
}

/// <summary>Stable codes the apps switch on (problem details "code").</summary>
public static class LiveChargingErrorCodes
{
    public const string CodeNotFound = "CODE_NOT_FOUND";
    public const string CodeRevoked = "CODE_REVOKED";
    public const string CodeMismatch = "CODE_MISMATCH";
    public const string IssueAlreadyOpen = "ISSUE_ALREADY_OPEN";
    public const string IssueWindowClosed = "ISSUE_WINDOW_CLOSED";
    public const string SessionOpen = "SESSION_OPEN";
    public const string ReceiptNotFound = "RECEIPT_NOT_FOUND";
}

/// <summary>Numbers of §8 (decisions 12 and 14) and the sticker / receipt link bases.</summary>
public static class LiveChargingLimits
{
    /// <summary>A driver may report a problem with a session up to this long after it ended (or while it runs).</summary>
    public static readonly TimeSpan IssueWindow = TimeSpan.FromDays(7);

    /// <summary>"May have a problem": this many open reports on one plug within <see cref="SuspectWindow"/>.</summary>
    public const int SuspectReports = 3;
    public static readonly TimeSpan SuspectWindow = TimeSpan.FromHours(1);

    /// <summary>One report per driver per plug in this window; a second tap returns the first report.</summary>
    public static readonly TimeSpan ReportDedupWindow = TimeSpan.FromMinutes(30);

    /// <summary>What the sticker's QR opens (App Link / Universal Link, web fallback otherwise).</summary>
    public const string StickerLinkBase = "https://cable-app.com/c/";

    /// <summary>What the receipt's QR opens: the public verification page (it reads GET /api/receipts/verify/{receiptNo}).</summary>
    public const string ReceiptVerifyLinkBase = "https://cable-app.com/r/";
}
