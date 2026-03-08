namespace Cable.Requests.Partners;

public record SetProviderCreditLimitRequest(string ProviderType, int ProviderId, decimal? CreditLimit);
