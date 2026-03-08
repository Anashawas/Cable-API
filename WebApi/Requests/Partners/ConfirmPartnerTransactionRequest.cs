namespace Cable.Requests.Partners;

public record InitiatePartnerTransactionRequest(
    int PartnerAgreementId,
    decimal TransactionAmount,
    string CurrencyCode
);
