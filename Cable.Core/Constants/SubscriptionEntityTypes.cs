namespace Cable.Core.Constants;

/// <summary>
/// What a Subscription is attached to. Strings, matching the ProviderType
/// convention used by offers and partner transactions.
/// </summary>
public static class SubscriptionEntityTypes
{
    public const string StationPremium = "StationPremium";
    public const string Banner = "Banner";
    public const string ServiceProviderPremium = "ServiceProviderPremium";

    /// <summary>Cable Connect: live charger data for a station (OCPP). EntityId = ChargingPoint.Id.</summary>
    public const string OcppConnect = "OcppConnect";

    public static readonly IReadOnlyList<string> All = [StationPremium, Banner, ServiceProviderPremium, OcppConnect];
    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
