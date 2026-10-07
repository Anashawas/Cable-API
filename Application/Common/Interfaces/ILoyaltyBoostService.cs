namespace Application.Common.Interfaces;

/// <summary>
/// The multiplier that applied, snapshotted at the moment it was resolved.
///
/// <paramref name="BoostId"/> is null for the welcome bonus, which is a property
/// of the app rather than a campaign and so has no row to point at.
/// </summary>
public record BoostResolution(int? BoostId, double Multiplier, bool IsWelcomeBonus);

public interface ILoyaltyBoostService
{
    /// <summary>
    /// The single best multiplier for this customer at this provider right now,
    /// or null when the charge earns its ordinary points.
    ///
    /// Two independent things are considered — a running campaign, and the
    /// once-ever welcome bonus. Neither stacks with the other: the higher of the
    /// two wins outright, so a first-time customer at a 3x station gets 3x, not
    /// 6x and not 2x.
    ///
    /// Called at scan rather than when the code is issued, because the customer
    /// is unknown until then and both conditions depend on them.
    /// </summary>
    /// <param name="excludeTransactionId">
    /// The transaction being completed. It has already been marked Completed by
    /// the caller, so the first-charge test must not count it as prior history.
    /// </param>
    Task<BoostResolution?> ResolveAsync(
        int userId,
        string providerType,
        int providerId,
        DateTime nowUtc,
        int? excludeTransactionId = null,
        CancellationToken cancellationToken = default);
}
