namespace Cable.Requests.Users;

/// <summary>
/// The request details for chaging a password
/// </summary>
/// <param name="Password">The new password of the user</param>
public record ChangePasswordRequest(string Password);
