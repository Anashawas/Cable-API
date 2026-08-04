namespace Cable.Requests.Offers;

public record UpdateSettlementStatusRequest(int Status, string? Note);
