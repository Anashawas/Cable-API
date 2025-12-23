namespace Application.Common.Models;

public record SmsResult(
    bool Success,
    string Message,
    string? ProviderId = null,
    DateTime? SentAt = null,
    int AttemptCount = 1
);

public record SmsDeliveryStatus(
    string ProviderId,
    string Status,
    DateTime StatusDate,
    string? ErrorMessage = null
);