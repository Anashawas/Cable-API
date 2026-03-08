namespace Cable.Requests.Providers;

/// <summary>
/// Provider authentication request - Step 1: Email and Password validation
/// </summary>
/// <param name="Email">Provider user email</param>
/// <param name="Password">Provider user password</param>
public record ProviderLoginRequest(string Email, string Password);
