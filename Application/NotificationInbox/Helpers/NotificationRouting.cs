namespace Application.NotificationInbox.Helpers;

/// <summary>
/// Single source of truth for notification routing (Part B R1/R2/R7).
/// From one (targetType, targetId) pair it builds everything both entry points
/// need: the inbox deepLink (in-app list tap) and the FCM data payload
/// (push tap), so console pushes, our pushes, and inbox taps all land on the
/// same screen. Senders never hand-type links.
/// </summary>
public static class NotificationRouting
{
    // Structured target types (admin sends, R4). Kebab-case, matches the deep-link host.
    public const string TargetChargingPoint = "charging-point";
    public const string TargetServiceProvider = "service-provider";
    public const string TargetNone = "none";

    // FCM string types the app's push handler routes on (keep console parity — R2).
    // "shops" is deliberately dropped from the contract (R6).
    public const string FcmTypeStations = "stations";
    public const string FcmTypeProviderAnnouncement = "provider_announcement";
    public const string FcmTypeGeneral = "general";

    public static bool IsValidTargetType(string? targetType) =>
        targetType is null or TargetNone or TargetChargingPoint or TargetServiceProvider;

    /// <summary>"ChargingPoint" | "ServiceProvider" (ProviderManagers convention) → target type.</summary>
    public static string ProviderTypeToTargetType(string providerType) =>
        providerType == "ServiceProvider" ? TargetServiceProvider : TargetChargingPoint;

    /// <summary>Canonical cable:// link — the id always travels as targetId (R7).</summary>
    public static string? BuildDeepLink(string? targetType, int? targetId) =>
        targetType switch
        {
            TargetChargingPoint when targetId.HasValue => $"cable://charging-point?targetId={targetId}",
            TargetServiceProvider when targetId.HasValue => $"cable://service-provider?targetId={targetId}",
            _ => null
        };

    /// <summary>
    /// FCM data payload for the push tap. type + chargerId/providerId are the
    /// canonical console-style routing; deepLink rides along as the app's
    /// belt-and-suspenders fallback (R2).
    /// </summary>
    public static Dictionary<string, string> BuildFcmData(string? targetType, int? targetId)
    {
        var data = new Dictionary<string, string>();
        var deepLink = BuildDeepLink(targetType, targetId);

        switch (targetType)
        {
            case TargetChargingPoint when targetId.HasValue:
                data["type"] = FcmTypeStations;
                data["chargerId"] = targetId.Value.ToString();
                break;
            case TargetServiceProvider when targetId.HasValue:
                data["type"] = FcmTypeProviderAnnouncement;
                data["providerId"] = targetId.Value.ToString();
                break;
            default:
                data["type"] = FcmTypeGeneral;
                break;
        }

        if (deepLink is not null)
            data["deepLink"] = deepLink;

        return data;
    }
}
