using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.ConfirmOfferTransaction;

public record ScanOfferCodeResult(
    string OfferTitle,
    int PointsDeducted,
    decimal MonetaryValue,
    string CurrencyCode
);

public record ScanOfferCodeCommand(string OfferCode) : IRequest<ScanOfferCodeResult>;

public class ScanOfferCodeCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    ILoyaltyPointService loyaltyPointService)
    : IRequestHandler<ScanOfferCodeCommand, ScanOfferCodeResult>
{
    public async Task<ScanOfferCodeResult> Handle(ScanOfferCodeCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var transaction = await applicationDbContext.OfferTransactions
                              .Include(x => x.Offer)
                              .FirstOrDefaultAsync(x => x.OfferCode == request.OfferCode
                                                        && !x.IsDeleted
                                                        && x.Status == (int)OfferTransactionStatus.Initiated,
                                  cancellationToken)
                          ?? throw new NotFoundException($"Initiated transaction with code '{request.OfferCode}' not found");

        // Check if code has expired
        if (DateTime.UtcNow > transaction.CodeExpiresAt)
        {
            transaction.Status = (int)OfferTransactionStatus.Expired;
            await applicationDbContext.SaveChanges(cancellationToken);
            throw new DataValidationException("OfferCode", "This offer code has expired");
        }

        // Check max uses per user for this offer
        var offer = transaction.Offer;
        if (offer.MaxUsesPerUser.HasValue)
        {
            var userUsageCount = await applicationDbContext.OfferTransactions
                .CountAsync(x => x.ProviderOfferId == transaction.ProviderOfferId
                                 && x.UserId == userId
                                 && !x.IsDeleted
                                 && x.Status == (int)OfferTransactionStatus.Completed,
                    cancellationToken);

            if (userUsageCount >= offer.MaxUsesPerUser.Value)
                throw new DataValidationException("Offer", "You have reached the maximum usage limit for this offer");
        }

        // Deduct points BEFORE marking as completed (if insufficient, transaction stays Initiated)
        if (transaction.PointsDeducted > 0)
        {
            await loyaltyPointService.DeductPointsFromOfferAsync(
                userId,
                transaction.PointsDeducted,
                transaction.ProviderType,
                transaction.ProviderId,
                transaction.Id,
                cancellationToken: cancellationToken);
        }

        // Set the user who scanned and complete the transaction
        transaction.UserId = userId;
        transaction.CompletedAt = DateTime.UtcNow;
        transaction.Status = (int)OfferTransactionStatus.Completed;

        await applicationDbContext.SaveChanges(cancellationToken);

        return new ScanOfferCodeResult(
            offer.Title ?? "Unknown",
            transaction.PointsDeducted,
            transaction.MonetaryValue,
            transaction.CurrencyCode);
    }
}
