using Cable.Core.Utilities;

namespace Application.EmergencyServices.Queries.GetAllEmergencyServices;

public record GetAllEmergencyServicesDto(
    int Id,
    string Title,
    string? Description,
    string? ImageUrl,
    int SubscriptionType,
    string? PriceDetails,
    string? ActionUrl,
    TimeSpan? OpenFrom,
    TimeSpan? OpenTo,
    string? PhoneNumber,
    string? WhatsAppNumber,
    bool IsActive,
    int SortOrder
)
{
    // Returned in dial-ready E.164 (+962...) so tel: links work without a
    // mobile release. Storage is unchanged (962... in the DB).
    public string? PhoneNumber { get; init; } = PhoneNumberUtility.ToE164OrOriginal(PhoneNumber);

    // wa.me requires bare digits with country code and NO leading + or 0.
    public string? WhatsAppNumber { get; init; } =
        PhoneNumberUtility.FormatForWhatsApp(WhatsAppNumber) ?? WhatsAppNumber;
}
