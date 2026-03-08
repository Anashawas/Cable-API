namespace Application.Offers.Queries.GetActiveOffers;

public record OfferDto(
    int Id,
    string Title,
    string? TitleAr,
    string? Description,
    string? DescriptionAr,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    int ProposedByUserId,
    string? ProposedByUserName,
    int ApprovalStatus,
    int PointsCost,
    decimal MonetaryValue,
    string CurrencyCode,
    int? MaxUsesPerUser,
    int? MaxTotalUses,
    int CurrentTotalUses,
    int OfferCodeExpiryMinutes,
    string? ImageUrl,
    DateTime ValidFrom,
    DateTime? ValidTo,
    bool IsActive,
    DateTime CreatedAt
);
