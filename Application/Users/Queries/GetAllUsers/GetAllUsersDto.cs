namespace Application.Users.Queries.GetAllUsers;

public record GetAllUsersDto(
    int Id,
    string? Name,
    string? Phone,
    string? UserName,
    string? Email,
    bool IsPhoneVerified,
    DateTime CreatedAt,
    RoleSummary Role,
    List<UserCarSummaryDto> UserCars);

/// <summary>
/// The summary of the role
/// </summary>
/// <param name="Id">The id of the role</param>
/// <param name="Name">The name of the role</param>
public record RoleSummary(int Id, string Name);

public record UserCarSummaryDto(
    int Id,
    string CarTypeName,
    string CarModelName,
    string? PlugTypeName,
    DateTime CreatedAt);
