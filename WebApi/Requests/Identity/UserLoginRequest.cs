namespace Cable.Requests.Identity;

/// <summary>
/// The login request
/// </summary>
/// <param name="Username">The user name</param>
/// <param name="Password">The password of the user</param>
public record LoginRequest(string Username, string Password);
