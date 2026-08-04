namespace Cable.Requests.Rates;

public record UpdateRateRequest(int ChargingPointRate, string? Comment = null);
