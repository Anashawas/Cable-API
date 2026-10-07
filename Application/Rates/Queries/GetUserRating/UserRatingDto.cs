namespace Application.Rates.Queries.GetUserRating;

/// <summary>One rating a provider left about a driver.</summary>
public record UserRatingEntryDto(
    int Id,
    int Rating,
    string? Comment,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    int PartnerTransactionId,
    DateTime CreatedAt);

/// <summary>
/// A driver's standing as a customer. <see cref="AverageRating"/> is computed on
/// read rather than denormalized onto each row (the approach Rate and
/// ServiceProviderRate take), because a stored average goes stale the moment a
/// rating is edited or soft-deleted and there is no volume here to justify it.
/// </summary>
public record UserRatingSummaryDto(
    int UserId,
    double? AverageRating,
    int RatingsCount,
    IReadOnlyList<UserRatingEntryDto> Ratings);
