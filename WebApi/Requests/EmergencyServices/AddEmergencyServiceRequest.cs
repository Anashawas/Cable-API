namespace Cable.Requests.EmergencyServices;

public record AddEmergencyServiceRequest(
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
