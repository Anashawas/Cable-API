namespace Cable.Requests.Users;

/// <summary>
/// The request details for validating a password reset code
/// </summary>
/// <param name="Email">The email address of the user</param>
/// <param name="Code">The 6-digit reset code sent via email</param>
public record ValidateResetCodeRequest(
    string Email,
    string Code
);
