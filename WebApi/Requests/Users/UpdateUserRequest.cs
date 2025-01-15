namespace Cable.Requests.Users;

/// <summary>
/// The request details for updating a user
/// </summary>
/// <param name="Name">The name of the user</param>
/// <param name="UserName">The username used for siging in</param>
/// <param name="Email">The email of the user</param>
/// <param name="RoleId">The role of the user</param>
/// <param name="IsActive">Determines if the user is active or not</param>
public record UpdateUserRequest(string Name, string UserName,string Phone,
    int RoleId,  bool IsActive,string? Email);
