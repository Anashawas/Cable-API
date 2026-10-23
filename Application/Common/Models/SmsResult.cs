namespace Application.Common.Models;

public record SmsResult(
    bool Success,
    string Message,
    string? ProviderId = null,
    DateTime? SentAt = null,
    int AttemptCount = 1
);

/// <summary>
/// The SMS account as the provider sees it (SMS April chk_user / chk_balance / senderstatus).
/// Balance is null when the provider did not return a number.
/// </summary>
public record SmsAccountStatus(
    bool AccountOk,
    string Account,
    bool SenderOk,
    string Sender,
    decimal? Balance,
    decimal MinBalance
);

public record SmsDeliveryStatus(
    string ProviderId,
    string Status,
    DateTime StatusDate,
    string? ErrorMessage = null
);