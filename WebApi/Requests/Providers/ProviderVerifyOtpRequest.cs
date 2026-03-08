namespace Cable.Requests.Providers;

/// <summary>
/// Provider OTP verification request - Step 3: Verify OTP and complete login
/// </summary>
/// <param name="SessionToken">Session token from Step 1 authentication</param>
/// <param name="OtpCode">6-digit OTP code sent to phone</param>
public record ProviderVerifyOtpRequest(string SessionToken, string OtpCode);
