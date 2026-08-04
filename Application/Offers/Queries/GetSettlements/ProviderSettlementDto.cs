namespace Application.Offers.Queries.GetSettlements;

public record ProviderDetailsDto(
    string ProviderType,
    int ProviderId,
    string? Name,
    string? Phone,
    string? Address,
    string? Icon
);

public record OwnerDetailsDto(
    int OwnerId,
    string? Name,
    string? Email,
    string? Phone
);

public record PartnerTransactionSummaryDto(
    int TransactionCount,
    decimal TransactionAmount,
    decimal CommissionAmount,
    int TotalPointsAwarded
);

public record OfferTransactionSummaryDto(
    int TransactionCount,
    decimal PaymentAmount,
    int TotalPointsDeducted
);

public record ProviderSettlementDto(
    int Id,
    ProviderDetailsDto ProviderDetails,
    OwnerDetailsDto OwnerDetails,
    int PeriodYear,
    int PeriodMonth,
    int PeriodType,
    int PeriodWeek,
    PartnerTransactionSummaryDto PartnerTransactions,
    OfferTransactionSummaryDto OfferTransactions,
    decimal NetBalance,
    decimal WalletApplied,
    decimal OutstandingAmount, // CommissionAmount - WalletApplied (> 0 means provider still owes)
    int SettlementStatus,
    DateTime? PaidAt,
    string? AdminNote,
    DateTime CreatedAt,
    decimal? CurrentWalletBalance = null
);
