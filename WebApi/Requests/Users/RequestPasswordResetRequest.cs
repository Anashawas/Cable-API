namespace Cable.Requests.Users;

/// <summary>
/// The request details for requesting a password reset
/// </summary>
/// <param name="Email">The email address of the user</param>
public record RequestPasswordResetRequest(string Email);
