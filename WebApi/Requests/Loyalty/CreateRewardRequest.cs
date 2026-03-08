namespace Cable.Requests.Loyalty;

public record CreateRewardRequest(
    string Name,
    string? Description,
    int PointsCost,
    int RewardType,
    string? RewardValue,
    string? ProviderType,
    int? ProviderId,
    int? ServiceCategoryId,
    int? MaxRedemptions,
    string? ImageUrl,
    DateTime ValidFrom,
    DateTime? ValidTo
);
