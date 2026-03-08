namespace Cable.Requests.Offers;

public record UpdateOfferRequest(
    string Title,
    string? TitleAr,
    string? Description,
    string? DescriptionAr,
    string ProviderType,
    int ProviderId,
    int PointsCost,
    decimal MonetaryValue,
    string CurrencyCode,
    int? MaxUsesPerUser,
    int? MaxTotalUses,
    int OfferCodeExpiryMinutes,
    string? ImageUrl,
    DateTime ValidFrom,
    DateTime? ValidTo,
    bool IsActive
);
