namespace Cable.Requests.Users;

/// <summary>
/// The request details for resetting password with code
/// </summary>
/// <param name="Email">The email address of the user</param>
/// <param name="Code">The 6-digit reset code sent via email</param>
/// <param name="NewPassword">The new password</param>
public record ResetPasswordWithCodeRequest(
    string Email,
    string Code,
    string NewPassword
);
