namespace Cable.Requests.ServiceProviders;

/// <summary>
/// Request to change the owner of a service provider
/// </summary>
/// <param name="NewOwnerId">The ID of the new owner user</param>
public record ChangeServiceProviderOwnerRequest(int NewOwnerId);
