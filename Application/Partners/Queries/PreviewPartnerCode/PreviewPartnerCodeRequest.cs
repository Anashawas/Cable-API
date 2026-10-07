using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.PreviewPartnerCode;

public record PreviewPartnerCodeResult(
    int TransactionId,
    string TransactionCode,
    string ProviderName,
    string ProviderType,
    int ProviderId,
    decimal TransactionAmount,
    string CurrencyCode,
    decimal CommissionAmount,
    /// <summary>Final points, multiplier already applied. Matches what the scan awards.</summary>
    int PointsToBeAwarded,
    DateTime CodeExpiresAt,
    int ExpiresInSeconds,
    bool CanConfirm,
    string? BlockReason,
    /// <summary>Points before any multiplier. Equals PointsToBeAwarded when none applies.</summary>
    int BasePoints,
    /// <summary>Null when no multiplier applies. 2.0 = double points.</summary>
    double? Multiplier,
    /// <summary>True when the multiplier is the once-ever first-charge welcome bonus.</summary>
    bool IsWelcomeBonus
);

public record PreviewPartnerCodeRequest(string TransactionCode) : IRequest<PreviewPartnerCodeResult>;

/// <summary>
/// Read-only companion to <c>ScanPartnerCodeCommand</c>: resolves a scanned
/// partner QR code to the details the user confirms against — who they are
/// paying, how much, and the points they earn — without completing the
/// transaction or awarding anything.
///
/// The mobile flow is scan -> preview -> user confirms -> ScanPartnerCode.
/// Nothing here writes, so abandoning the confirmation sheet leaves the
/// transaction Initiated and scannable until it expires.
/// </summary>
public class PreviewPartnerCodeRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    ILoyaltyBoostService loyaltyBoostService)
    : IRequestHandler<PreviewPartnerCodeRequest, PreviewPartnerCodeResult>
{
    public async Task<PreviewPartnerCodeResult> Handle(PreviewPartnerCodeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var transaction = await applicationDbContext.PartnerTransactions
                              .AsNoTracking()
                              .FirstOrDefaultAsync(x => x.TransactionCode == request.TransactionCode
                                                        && !x.IsDeleted
                                                        && x.Status == (int)PartnerTransactionStatus.Initiated,
                                  cancellationToken)
                          ?? throw new NotFoundException($"Initiated partner transaction with code '{request.TransactionCode}' not found");

        // An expired code is reported, not settled. Reversing the provider's
        // reserved commission is a write, and a preview must not mutate — the
        // "expire-partner-transaction-codes" Hangfire job (every 5 minutes) and
        // ScanPartnerCode itself both already perform that refund.
        var now = DateTime.UtcNow;
        if (now > transaction.CodeExpiresAt)
            throw new DataValidationException("TransactionCode", "This transaction code has expired");

        // The stored figure is the unboosted baseline — the code was issued
        // before the customer was known, so no multiplier could be resolved
        // then. Resolve it here with the same service the scan uses, or the
        // confirmation sheet would promise 25 and the scan would award 50.
        var basePoints = transaction.PointsAwarded ?? 0;

        var boost = await loyaltyBoostService.ResolveAsync(
            userId,
            transaction.ProviderType,
            transaction.ProviderId,
            now,
            excludeTransactionId: transaction.Id,
            cancellationToken: cancellationToken);

        var pointsToBeAwarded = boost is null
            ? basePoints
            : (int)Math.Floor(basePoints * boost.Multiplier);

        // Surface a loyalty block here rather than letting the user confirm and
        // hit the failure inside the award step, which would roll the whole
        // scan back after they had already committed to it.
        var blockReason = await GetLoyaltyBlockReason(userId, pointsToBeAwarded, cancellationToken);

        string? providerName = null;
        if (transaction.ProviderType == "ChargingPoint")
        {
            providerName = await applicationDbContext.ChargingPoints
                .Where(x => x.Id == transaction.ProviderId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (transaction.ProviderType == "ServiceProvider")
        {
            providerName = await applicationDbContext.ServiceProviders
                .Where(x => x.Id == transaction.ProviderId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // Floor at 0: the expiry check above guarantees a positive window, but
        // rounding on a sub-second remainder must not hand the client a
        // negative countdown.
        var expiresInSeconds = (int)Math.Max(0, Math.Floor((transaction.CodeExpiresAt - now).TotalSeconds));

        return new PreviewPartnerCodeResult(
            transaction.Id,
            transaction.TransactionCode,
            providerName ?? "Unknown",
            transaction.ProviderType,
            transaction.ProviderId,
            transaction.TransactionAmount ?? 0,
            transaction.CurrencyCode ?? "",
            transaction.CommissionAmount ?? 0,
            pointsToBeAwarded,
            transaction.CodeExpiresAt,
            expiresInSeconds,
            blockReason is null,
            blockReason,
            basePoints,
            boost?.Multiplier,
            boost?.IsWelcomeBonus ?? false);
    }

    /// <summary>
    /// Mirrors the block rule enforced by LoyaltyPointService when points are
    /// awarded, including the "expired block counts as unblocked" allowance.
    /// A transaction worth no points never reaches that check, so it is never
    /// reported as blocked.
    /// </summary>
    private async Task<string?> GetLoyaltyBlockReason(int userId, int pointsToBeAwarded,
        CancellationToken cancellationToken)
    {
        if (pointsToBeAwarded <= 0)
            return null;

        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, cancellationToken);

        if (wallet is not { IsBlocked: true })
            return null;

        if (wallet.BlockedUntil.HasValue && wallet.BlockedUntil.Value < DateTime.UtcNow)
            return null;

        return "Your loyalty account is currently blocked. Please contact support.";
    }
}
