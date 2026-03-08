namespace Application.Common.Interfaces;

public interface ILoyaltyPointService
{
    /// <summary>
    /// Award fixed points from LoyaltyPointAction table (e.g., RATE_STATION, ADD_FAVORITE)
    /// </summary>
    Task<int> AwardPointsAsync(
        int userId,
        string actionCode,
        string? referenceType = null,
        int? referenceId = null,
        string? note = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Award dynamic points from partner transactions (calculated from transaction amount)
    /// </summary>
    Task<int> AwardPointsFromOfferAsync(
        int userId,
        int calculatedPoints,
        string providerType,
        int providerId,
        int offerTransactionId,
        string? note = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deduct points when user redeems an offer (spends points)
    /// </summary>
    Task<int> DeductPointsFromOfferAsync(
        int userId,
        int pointsToDeduct,
        string providerType,
        int providerId,
        int offerTransactionId,
        string? note = null,
        CancellationToken cancellationToken = default);
}
