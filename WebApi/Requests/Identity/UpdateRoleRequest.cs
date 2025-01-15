namespace Cable.Requests.Identity;

/// <summary>
/// The role to update
/// </summary>
/// <param name="Name">The name of the role</param>
/// <param name="Description">The description of the role</param>
/// <param name="Privileges">The associated privileges Ids</param>
public record UpdateRoleRequest(string Name, string Description, List<int> Privileges);
