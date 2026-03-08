namespace Cable.Requests.Loyalty;

public record UpdateRewardRequest(
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
    bool IsActive,
    DateTime ValidFrom,
    DateTime? ValidTo
);
