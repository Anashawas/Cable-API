namespace Cable.Requests.Identity;

/// <summary>
/// The login request
/// </summary>
/// <param name="Email">The user name</param>
/// <param name="Password">The password of the user</param>
public record LoginRequest(string Email, string Password);
