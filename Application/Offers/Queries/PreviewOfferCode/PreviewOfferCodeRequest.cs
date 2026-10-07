using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.PreviewOfferCode;

public record PreviewOfferCodeResult(
    int TransactionId,
    string OfferCode,
    int OfferId,
    string OfferTitle,
    string? OfferTitleAr,
    string? OfferImageUrl,
    string ProviderName,
    string ProviderType,
    int ProviderId,
    /// <summary>Points this redemption will cost. Matches what the scan deducts.</summary>
    int PointsToBeDeducted,
    decimal MonetaryValue,
    string CurrencyCode,
    /// <summary>The user's spendable balance right now.</summary>
    int CurrentPointsBalance,
    /// <summary>
    /// How many points short the user is, or 0 when they can afford it. Lets the
    /// app show "you need 50 more" rather than a bare refusal.
    /// </summary>
    int PointsShortfall,
    DateTime CodeExpiresAt,
    int ExpiresInSeconds,
    bool CanConfirm,
    string? BlockReason
);

public record PreviewOfferCodeRequest(string OfferCode) : IRequest<PreviewOfferCodeResult>;

/// <summary>
/// Read-only companion to <c>ScanOfferCodeCommand</c>: resolves a scanned offer
/// QR code to the details the user confirms against — what they are buying, what
/// it costs them, and whether they can actually afford it — without deducting
/// anything.
///
/// The mobile flow is scan -> preview -> user confirms -> ScanOfferCode.
///
/// This matters more here than on the charging side. A partner scan only ever
/// gives the user points; an offer scan spends them, and the balance can be
/// short. Without a preview the user finds that out only after committing.
/// </summary>
public class PreviewOfferCodeRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<PreviewOfferCodeRequest, PreviewOfferCodeResult>
{
    public async Task<PreviewOfferCodeResult> Handle(PreviewOfferCodeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var transaction = await applicationDbContext.OfferTransactions
                              .AsNoTracking()
                              .Include(x => x.Offer)
                              .FirstOrDefaultAsync(x => x.OfferCode == request.OfferCode
                                                        && !x.IsDeleted
                                                        && x.Status == (int)OfferTransactionStatus.Initiated,
                                  cancellationToken)
                          ?? throw new NotFoundException($"Initiated offer transaction with code '{request.OfferCode}' not found");

        // An expired code is reported, not settled. ScanOfferCode flips the row to
        // Expired when it sees this, but a preview must not mutate — the user may
        // never confirm, and the scan itself will do it if they do.
        var now = DateTime.UtcNow;
        if (now > transaction.CodeExpiresAt)
            throw new DataValidationException("OfferCode", "This offer code has expired");

        var offer = transaction.Offer;
        var pointsToBeDeducted = transaction.PointsDeducted;

        var wallet = await applicationDbContext.UserLoyaltyAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.UserId == userId && !w.IsDeleted, cancellationToken);

        var currentBalance = wallet?.CurrentBalance ?? 0;

        var blockReason = await GetBlockReason(
            userId, transaction, offer, pointsToBeDeducted, currentBalance, wallet, cancellationToken);

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

        return new PreviewOfferCodeResult(
            transaction.Id,
            transaction.OfferCode,
            offer.Id,
            offer.Title,
            offer.TitleAr,
            offer.ImageUrl,
            providerName ?? "Unknown",
            transaction.ProviderType,
            transaction.ProviderId,
            pointsToBeDeducted,
            transaction.MonetaryValue,
            transaction.CurrencyCode,
            currentBalance,
            Math.Max(0, pointsToBeDeducted - currentBalance),
            transaction.CodeExpiresAt,
            expiresInSeconds,
            blockReason is null,
            blockReason);
    }

    /// <summary>
    /// Mirrors every rule ScanOfferCode enforces, so nothing the user can hit on
    /// confirm is invisible here. Ordered most-fundamental first: a blocked
    /// account is worth reporting even when the balance is also short, because
    /// topping up would not help.
    /// </summary>
    private async Task<string?> GetBlockReason(
        int userId,
        OfferTransaction transaction,
        ProviderOffer offer,
        int pointsToBeDeducted,
        int currentBalance,
        UserLoyaltyAccount? wallet,
        CancellationToken cancellationToken)
    {
        // A redemption costing nothing never reaches the deduction path, so the
        // block and balance rules do not apply to it.
        if (pointsToBeDeducted > 0)
        {
            var blockedNow = wallet is { IsBlocked: true }
                             && !(wallet.BlockedUntil.HasValue && wallet.BlockedUntil.Value < DateTime.UtcNow);

            if (blockedNow)
                return "Your loyalty account is currently blocked. Please contact support.";

            if (currentBalance < pointsToBeDeducted)
                return $"Insufficient points balance. Required: {pointsToBeDeducted}, Available: {currentBalance}";
        }

        if (offer.MaxUsesPerUser.HasValue)
        {
            var userUsageCount = await applicationDbContext.OfferTransactions
                .AsNoTracking()
                .CountAsync(x => x.ProviderOfferId == transaction.ProviderOfferId
                                 && x.UserId == userId
                                 && !x.IsDeleted
                                 && x.Status == (int)OfferTransactionStatus.Completed,
                    cancellationToken);

            if (userUsageCount >= offer.MaxUsesPerUser.Value)
                return "You have reached the maximum usage limit for this offer";
        }

        return null;
    }
}
