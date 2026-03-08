namespace Cable.Requests.ServiceProviders;

public record UpdateServiceProviderRequest(
    string Name,
    int ServiceCategoryId,
    int StatusId,
    string? Description,
    string? Phone,
    string? OwnerPhone,
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
    bool IsVerified,
    bool HasOffer,
    string? OfferDescription,
    string? Service,
    string? Icon,
    string? Note,
    string? WhatsAppNumber,
    string? WebsiteUrl
);
