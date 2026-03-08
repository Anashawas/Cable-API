namespace Cable.Requests.Providers;

/// <summary>
/// Provider OTP send request - Step 2: Send OTP to verified phone
/// </summary>
/// <param name="SessionToken">Session token from Step 1 authentication</param>
public record ProviderSendOtpRequest(string SessionToken);
