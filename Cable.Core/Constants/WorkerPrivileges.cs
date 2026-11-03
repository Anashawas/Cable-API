namespace Cable.Core.Constants;

/// <summary>
/// What a worker (ProviderManager) may do in the partner app, chosen by the owner.
/// Stored comma-separated in ProviderManager.Privileges, always as the explicit list: a key added later is never
/// switched on by itself (WORKER_TRACKER decision 16b). null = the pre-privileges set (<see cref="Legacy"/>);
/// "" = nothing (station details only). The owner and admins always have all of them.
/// What the worker can actually use is granted ∩ allowed by the station's plan — see Application.Workers.WorkerAccess.
/// </summary>
public static class WorkerPrivileges
{
    /// <summary>Submit station edit requests (station details are always visible).</summary>
    public const string ManageStation = "ManageStation";
    /// <summary>Generate QR / create and cancel point-of-sale transactions. Enforced.</summary>
    public const string PointOfSale = "PointOfSale";
    public const string ManageOffers = "ManageOffers";
    public const string ViewWallet = "ViewWallet";
    public const string SendAnnouncements = "SendAnnouncements";
    public const string ViewFeedback = "ViewFeedback";
    public const string ViewStatistics = "ViewStatistics";
    /// <summary>Cable Connect: live picture, sessions, command history, cards list. Enforced.</summary>
    public const string ConnectView = "ConnectView";
    /// <summary>Cable Connect: start / stop / unlock / availability / reset / refresh, cards, sharing switch, cabinet names. Enforced.</summary>
    public const string ConnectControl = "ConnectControl";

    // Worker tracker (step 2+). Premium and above only.
    /// <summary>Punch in / out, see own shifts and leave. Enforced.</summary>
    public const string Attendance = "Attendance";
    /// <summary>May switch on fingerprint / face sign-in on their phone.</summary>
    public const string BiometricLogin = "BiometricLogin";
    /// <summary>"Who is in" and the attendance table of every worker. Enforced.</summary>
    public const string ViewAttendance = "ViewAttendance";
    /// <summary>Approve / reject leave requests. Enforced.</summary>
    public const string ApproveLeave = "ApproveLeave";
    /// <summary>Start / end another worker's shift by hand (never their own). Enforced.</summary>
    public const string ManageAttendance = "ManageAttendance";

    /// <summary>The keys that existed before the worker tracker: what a null stored value means.</summary>
    public static readonly string[] Legacy =
    [
        ManageStation, PointOfSale, ManageOffers, ViewWallet, SendAnnouncements, ViewFeedback, ViewStatistics, ConnectView, ConnectControl,
    ];

    public static readonly string[] All =
    [
        ManageStation, PointOfSale, ManageOffers, ViewWallet, SendAnnouncements, ViewFeedback, ViewStatistics, ConnectView, ConnectControl,
        Attendance, BiometricLogin, ViewAttendance, ApproveLeave, ManageAttendance,
    ];

    /// <summary>What a station's plan must have for a privilege to work (confirmed 2026-11-03).</summary>
    public enum Requirement { None, Loyalty, Workers, Connect, Biometrics }

    public static Requirement RequirementOf(string key) => key switch
    {
        ManageStation or ViewFeedback => Requirement.None,
        PointOfSale => Requirement.Loyalty,
        ConnectView or ConnectControl => Requirement.Connect,
        BiometricLogin => Requirement.Biometrics,
        _ => Requirement.Workers,
    };

    public static bool IsKnown(string key) => All.Contains(key, StringComparer.Ordinal);

    /// <summary>null = the legacy set; "" or whitespace = none; otherwise the listed known keys.</summary>
    public static IReadOnlyList<string> Parse(string? stored) =>
        stored is null
            ? Legacy
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(IsKnown).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The stored form: the known keys in catalog order, always explicit.</summary>
    public static string Format(IEnumerable<string> keys)
    {
        var set = keys.Where(IsKnown).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        return string.Join(",", All.Where(set.Contains));
    }

    public static bool Has(string? stored, string privilege) => Parse(stored).Contains(privilege, StringComparer.Ordinal);

    /// <summary>True when the worker holds every key.</summary>
    public static bool UsesAll(string? stored) => All.All(Parse(stored).Contains);
}
