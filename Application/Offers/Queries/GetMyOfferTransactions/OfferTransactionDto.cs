namespace Application.Offers.Queries.GetMyOfferTransactions;

public record OfferTransactionDto(
    int Id,
    int ProviderOfferId,
    string? OfferTitle,
    int? UserId,
    string? UserName,
    string OfferCode,
    int Status,
    int PointsDeducted,
    decimal MonetaryValue,
    string CurrencyCode,
    string ProviderType,
    int ProviderId,
    int? ConfirmedByUserId,
    DateTime CodeExpiresAt,
    DateTime? CompletedAt,
    DateTime CreatedAt
);
