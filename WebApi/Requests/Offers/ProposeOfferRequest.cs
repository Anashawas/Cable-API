namespace Cable.Requests.Offers;

public record ProposeOfferRequest(
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
    int OfferCodeExpirySeconds,
    string? ImageUrl,
    DateTime ValidFrom,
    DateTime? ValidTo,
    decimal? PointsPriceValue = null
);
