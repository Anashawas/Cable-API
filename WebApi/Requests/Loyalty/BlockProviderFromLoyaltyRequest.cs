namespace Cable.Requests.Loyalty;

public record BlockProviderFromLoyaltyRequest(
    string ProviderType, int ProviderId, string Reason, DateTime? BlockUntil = null);
