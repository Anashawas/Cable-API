namespace Cable.Requests.Offers;

public record UpdateSettlementStatusRequest(int Status, decimal? PaidAmount, string? Note);
