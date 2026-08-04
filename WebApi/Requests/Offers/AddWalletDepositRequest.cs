namespace Cable.Requests.Offers;

public record AddWalletDepositRequest(
    string ProviderType,
    int ProviderId,
    decimal Amount,
    int TransactionType,
    string? Note
);
