namespace Application.Common.Models;

/// <summary>
/// Result of Provider email/password authentication (Step 1 of 2FA)
/// Contains session token to be used for OTP sending and verification
/// </summary>
public record ProviderAuthSessionResult(
    bool Success,
    string Message,
    string SessionToken,
    string PhoneMasked,
    DateTime ExpiresAt
);
