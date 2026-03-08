namespace Cable.Requests.Partners;

public record RecordProviderPaymentRequest(string ProviderType, int ProviderId, decimal Amount, string? Note);
