namespace Cable.Core.Constants;

/// <summary>
/// What a worker (ProviderManager) may do in the partner app, chosen by the owner.
/// Stored comma-separated in ProviderManager.Privileges. null = everything (never set — the
/// behaviour before privileges existed); "" = nothing (the owner switched everything off).
/// The owner and admins always have all of them.
/// The UI hides what is missing; the API enforces the ones marked "enforced".
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

    public static readonly string[] All =
    [
        ManageStation, PointOfSale, ManageOffers, ViewWallet, SendAnnouncements, ViewFeedback, ViewStatistics, ConnectView, ConnectControl,
    ];

    public static bool IsKnown(string key) => All.Contains(key, StringComparer.Ordinal);

    /// <summary>null = all privileges (never restricted); "" or whitespace = none; otherwise the listed known keys.</summary>
    public static IReadOnlyList<string> Parse(string? stored) =>
        stored is null
            ? All
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(IsKnown).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Stores the explicit list; an "everything" selection is stored as null so new privileges apply automatically.</summary>
    public static string? Format(IEnumerable<string> keys)
    {
        var set = keys.Where(IsKnown).Distinct(StringComparer.Ordinal).ToList();
        return set.Count >= All.Length ? null : string.Join(",", All.Where(set.Contains));
    }

    public static bool Has(string? stored, string privilege) => Parse(stored).Contains(privilege, StringComparer.Ordinal);

    /// <summary>True only when the worker was never restricted (null) or holds every key.</summary>
    public static bool UsesAll(string? stored) => stored is null || All.All(Parse(stored).Contains);
}
