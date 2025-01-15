namespace Application.Common.Models;

/// <summary>
/// The user login result
/// </summary>
/// <param name="Id">The id of the user</param>
/// <param name="Username">The user name</param>
/// <param name="Name">The display name</param>
/// <param name="IsActiveDirectory">Is Active Directory User</param>
/// <param name="AccessToken">The access token</param>
/// <param name="RefreshToken">The refresh token</param>
/// <param name="Privileges">The user privleges</param>
public record UserLoginResult(
    int Id,
    string Username,
    string Name,
    string AccessToken,
    string RefreshToken,
    IReadOnlyCollection<string> Privileges
);

/// <summary>
/// The user login result
/// </summary>
/// <param name="Id">The id of the user</param>
/// <param name="Username">The user name</param>
/// <param name="Name">The display name</param>
/// <param name="IsActiveDirectory">Is Active Directory User</param>
/// <param name="Privileges">The user privleges</param>

public record UserLoginByTokenResult(
    int Id,
    string Username,
    string Name,
    IReadOnlyCollection<string> Privileges
);