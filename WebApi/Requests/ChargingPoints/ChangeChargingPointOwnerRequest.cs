namespace Cable.Requests.ChargingPoints;

/// <summary>
/// Request to change the owner of a charging point
/// </summary>
/// <param name="NewOwnerId">The ID of the new owner user</param>
public record ChangeChargingPointOwnerRequest(int NewOwnerId);
