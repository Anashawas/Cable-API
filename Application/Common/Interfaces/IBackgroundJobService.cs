namespace Application.Common.Interfaces;

public interface IBackgroundJobService
{
    // ==========================================
    // Transaction Code Expiry
    // ==========================================
    Task<int> ExpireOfferTransactionCodesAsync(CancellationToken cancellationToken = default);
    Task<int> ExpirePartnerTransactionCodesAsync(CancellationToken cancellationToken = default);

    // ==========================================
    // Settlement
    // ==========================================
    Task<int> GenerateMonthlySettlementsAsync(int year, int month, CancellationToken cancellationToken = default);

    // ==========================================
    // Security Cleanup (Critical)
    // ==========================================
    Task<int> CleanupExpiredPhoneVerificationsAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredPasswordResetsAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredOtpRateLimitsAsync(CancellationToken cancellationToken = default);

    // ==========================================
    // Business Expiry (Important)
    // ==========================================
    Task<int> DeactivateExpiredOffersAsync(CancellationToken cancellationToken = default);
    Task<int> DeactivateExpiredSharedLinksAsync(CancellationToken cancellationToken = default);
    Task<int> EndExpiredLoyaltySeasonsAsync(CancellationToken cancellationToken = default);
    Task<int> ExpireLoyaltyPointsAsync(CancellationToken cancellationToken = default);
    Task<int> DeactivateExpiredRewardsAsync(CancellationToken cancellationToken = default);
    Task<int> UnblockExpiredLoyaltyBlocksAsync(CancellationToken cancellationToken = default);
    Task<int> UnblockExpiredProviderLoyaltyBlocksAsync(CancellationToken cancellationToken = default);
}
