using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.InitiateOfferTransaction;

public record InitiateOfferTransactionResult(
    int Id,
    string OfferCode,
    DateTime ExpiresAt,
    int PointsCost,
    decimal MonetaryValue,
    string CurrencyCode
);

public record InitiateOfferTransactionCommand(
    int OfferId
) : IRequest<InitiateOfferTransactionResult>;

public class InitiateOfferTransactionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<InitiateOfferTransactionCommand, InitiateOfferTransactionResult>
{
    public async Task<InitiateOfferTransactionResult> Handle(InitiateOfferTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var staffUserId = currentUserService.UserId
                          ?? throw new NotAuthorizedAccessException("User not authenticated");

        var offer = await applicationDbContext.ProviderOffers
                        .FirstOrDefaultAsync(x => x.Id == request.OfferId
                                                   && !x.IsDeleted
                                                   && x.IsActive
                                                   && x.ApprovalStatus == (int)OfferApprovalStatus.Approved,
                            cancellationToken)
                    ?? throw new NotFoundException($"Active offer with id {request.OfferId} not found");

        // The offer names the provider, so the caller has to be checked against
        // it — otherwise any signed-in account could mint a code for anyone.
        await ProviderAccessGuard.EnsureCanActForProviderAsync(
            applicationDbContext, currentUserService, offer.ProviderType, offer.ProviderId,
            cancellationToken);

        // Check if provider is blocked from loyalty
        if (offer.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == offer.ProviderId && !x.IsDeleted, cancellationToken);
            if (cp is { IsLoyaltyBlocked: true } &&
                (!cp.LoyaltyBlockedUntil.HasValue || cp.LoyaltyBlockedUntil.Value >= DateTime.UtcNow))
                throw new DataValidationException("Loyalty", "This provider is currently blocked from the loyalty system");
        }
        else if (offer.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == offer.ProviderId && !x.IsDeleted, cancellationToken);
            if (sp is { IsLoyaltyBlocked: true } &&
                (!sp.LoyaltyBlockedUntil.HasValue || sp.LoyaltyBlockedUntil.Value >= DateTime.UtcNow))
                throw new DataValidationException("Loyalty", "This provider is currently blocked from the loyalty system");
        }

        // Check validity dates
        var now = DateTime.UtcNow;
        if (now < offer.ValidFrom)
            throw new DataValidationException("Offer", "This offer is not yet available");

        if (offer.ValidTo.HasValue && now > offer.ValidTo.Value)
            throw new DataValidationException("Offer", "This offer has expired");

        // Check max total uses
        if (offer.MaxTotalUses.HasValue && offer.CurrentTotalUses >= offer.MaxTotalUses.Value)
            throw new DataValidationException("Offer", "This offer has reached its maximum usage limit");

        // Atomically increment usage counter (prevents race condition)
        var updated = await applicationDbContext.ProviderOffers
            .Where(x => x.Id == offer.Id
                         && (x.MaxTotalUses == null || x.CurrentTotalUses < x.MaxTotalUses))
            .ExecuteUpdateAsync(x => x
                .SetProperty(o => o.CurrentTotalUses, o => o.CurrentTotalUses + 1), cancellationToken);

        if (updated == 0)
            throw new DataValidationException("Offer", "This offer has reached its maximum usage limit");

        // Generate unique offer code
        var offerCode = await GenerateUniqueCode(cancellationToken);
        var expiresAt = now.AddSeconds(offer.OfferCodeExpirySeconds);

        var transaction = new OfferTransaction
        {
            ProviderOfferId = offer.Id,
            OfferCode = offerCode,
            Status = (int)OfferTransactionStatus.Initiated,
            PointsDeducted = offer.PointsCost,
            MonetaryValue = offer.MonetaryValue,
            CurrencyCode = offer.CurrencyCode,
            ProviderType = offer.ProviderType,
            ProviderId = offer.ProviderId,
            ConfirmedByUserId = staffUserId,
            CodeExpiresAt = expiresAt
        };

        applicationDbContext.OfferTransactions.Add(transaction);
        await applicationDbContext.SaveChanges(cancellationToken);

        return new InitiateOfferTransactionResult(
            transaction.Id, offerCode, expiresAt, offer.PointsCost, offer.MonetaryValue, offer.CurrencyCode);
    }

    private async Task<string> GenerateUniqueCode(CancellationToken cancellationToken)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        string code;

        do
        {
            var randomPart = new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
            code = $"CBL-{randomPart}";
        } while (await applicationDbContext.OfferTransactions
                     .AnyAsync(x => x.OfferCode == code, cancellationToken));

        return code;
    }
}
