namespace Cable.Requests.Loyalty;

public record BlockUserFromLoyaltyRequest(int UserId, string Reason, DateTime? BlockUntil = null);
