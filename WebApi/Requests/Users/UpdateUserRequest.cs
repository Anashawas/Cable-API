namespace Cable.Requests.Users;

/// <summary>
/// The request details for updating a user
/// </summary>
/// <param name="Name">The name of the user</param>
/// <param name="Email">The email of the user</param>
/// <param name="RoleId">The role of the user</param>
/// <param name="IsActive">Determines if the user is active or not</param>
/// <param name="Country">The country of the user</param>
/// <param name="City">The city of the user</param>
public record UpdateUserRequest(string? Name,
    int RoleId, bool IsActive, string? Email,
    string? Country,
    string? City
    );
