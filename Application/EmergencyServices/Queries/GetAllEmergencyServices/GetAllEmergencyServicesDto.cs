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
);
