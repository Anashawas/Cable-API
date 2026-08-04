namespace Cable.Requests.ChargingPoints;

/// <summary>
/// Request to record a premium payment for a station.
/// </summary>
/// <param name="PaymentDate">When the owner paid for premium placement (UTC)</param>
/// <param name="ExpiresAt">When the premium period ends (UTC)</param>
/// <param name="Amount">Amount paid for this period</param>
/// <param name="Note">Optional note (e.g. payment reference)</param>
public record SetStationPremiumRequest(
    DateTime PaymentDate,
    DateTime ExpiresAt,
    decimal? Amount,
    string? Note);
