namespace Cable.Requests.Loyalty;

public record AdminAdjustPointsRequest(int UserId, int Points, string? Note);
