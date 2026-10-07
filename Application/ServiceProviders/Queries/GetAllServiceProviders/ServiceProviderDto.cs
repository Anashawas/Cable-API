using Cable.Core.Utilities;

namespace Application.ServiceProviders.Queries.GetAllServiceProviders;

public record ServiceProviderDto(
    int Id,
    string Name,
    int? OwnerId,
    string? OwnerName,
    int ServiceCategoryId,
    string? ServiceCategoryName,
    string? ServiceCategoryNameAr,
    int StatusId,
    string? StatusName,
    string? Description,
    string? Phone,
    string? Address,
    string? CountryName,
    string? CityName,
    double Latitude,
    double Longitude,
    double? Price,
    string? PriceDescription,
    string? FromTime,
    string? ToTime,
    string? MethodPayment,
    int VisitorsCount,
    bool IsVerified,
    bool HasOffer,
    string? OfferDescription,
    string? Service,
    string? Icon,
    string? WhatsAppNumber,
    string? WebsiteUrl,
    double AvgRating,
    int RateCount,
    List<string> Images,
    DateTime CreatedAt,
    bool IsPartner,
    int? FavoritesCount = null,
    /// <summary>
    /// The provider's currently redeemable offers, cheapest first. Populated by
    /// the list/detail queries via ProviderOfferSummaryLoader. Distinct from the
    /// legacy HasOffer/OfferDescription free-text fields on the entity, which
    /// are not backed by the ProviderOffer table.
    /// </summary>
    List<Offers.Queries.Common.ProviderOfferSummaryDto>? Offers = null
)
{
    // Returned in dial-ready E.164 (+962...) so tel: links work without a
    // mobile release. Storage is unchanged (962... in the DB).
    public string? Phone { get; init; } = PhoneNumberUtility.ToE164OrOriginal(Phone);

    // wa.me requires bare digits with country code and NO leading + or 0.
    public string? WhatsAppNumber { get; init; } =
        PhoneNumberUtility.FormatForWhatsApp(WhatsAppNumber) ?? WhatsAppNumber;
}
