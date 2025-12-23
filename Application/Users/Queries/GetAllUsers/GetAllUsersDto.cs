namespace Application.Users.Queries.GetAllUsers;

public record GetAllUsersDto(
    int Id,
    string? Name,
    string? Phone,
    string? UserName,
    string? Email,
    bool IsPhoneVerified,
    RoleSummary Role);

/// <summary>
/// The summary of the role
/// </summary>
/// <param name="Id">The id of the role</param>
/// <param name="Name">The name of the role</param>
public record RoleSummary(int Id, string Name);

