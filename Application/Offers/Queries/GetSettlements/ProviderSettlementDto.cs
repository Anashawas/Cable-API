namespace Application.Offers.Queries.GetSettlements;

public record ProviderSettlementDto(
    int Id,
    string ProviderType,
    int ProviderId,
    int ProviderOwnerId,
    string? ProviderOwnerName,
    int PeriodYear,
    int PeriodMonth,
    int PartnerTransactionCount,
    decimal PartnerTransactionAmount,
    decimal PartnerCommissionAmount,
    int TotalPointsAwarded,
    int OfferTransactionCount,
    decimal OfferPaymentAmount,
    int TotalPointsDeducted,
    decimal NetAmountDueToProvider,
    int SettlementStatus,
    DateTime? InvoicedAt,
    DateTime? PaidAt,
    decimal? PaidAmount,
    string? AdminNote,
    DateTime CreatedAt
);
