using Application.Common.Extensions;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.ConfirmOfferTransaction;

public record ScanOfferCodeResult(
    string OfferTitle,
    int PointsDeducted,
    decimal MonetaryValue,
    string CurrencyCode,
    decimal WalletCreditedAmount
);

public record ScanOfferCodeCommand(string OfferCode) : IRequest<ScanOfferCodeResult>;

public class ScanOfferCodeCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    ILoyaltyPointService loyaltyPointService,
    ISettlementService settlementService)
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

        await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Deduct points inside transaction (rolls back if wallet credit fails)
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

            // Credit the provider's wallet with the offer MonetaryValue (Cable pays provider)
            decimal walletBalanceAfter = 0;
            var monetaryValue = transaction.MonetaryValue;

            if (transaction.ProviderType == "ChargingPoint")
            {
                var cp = await applicationDbContext.ChargingPoints
                    .FindWithLockAsync("ChargingPoint", transaction.ProviderId, cancellationToken);
                if (cp != null)
                {
                    cp.WalletBalance += monetaryValue;
                    walletBalanceAfter = cp.WalletBalance;
                }
            }
            else if (transaction.ProviderType == "ServiceProvider")
            {
                var sp = await applicationDbContext.ServiceProviders
                    .FindWithLockAsync("ServiceProvider", transaction.ProviderId, cancellationToken);
                if (sp != null)
                {
                    sp.WalletBalance += monetaryValue;
                    walletBalanceAfter = sp.WalletBalance;
                }
            }

            // Track how much was credited to wallet for this transaction
            transaction.WalletCreditedAmount = monetaryValue;

            // Set the user who scanned and complete the transaction
            transaction.UserId = userId;
            transaction.CompletedAt = DateTime.UtcNow;
            transaction.Status = (int)OfferTransactionStatus.Completed;

            // Auto-update settlement for this transaction (includes WalletCreditedAmount)
            await settlementService.UpsertSettlementForOfferTransactionAsync(transaction, cancellationToken);

            await applicationDbContext.SaveChanges(cancellationToken);

            // Create wallet audit record (transaction.Id now available)
            if (monetaryValue > 0)
            {
                applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                {
                    ProviderType = transaction.ProviderType,
                    ProviderId = transaction.ProviderId,
                    TransactionType = (int)WalletTransactionType.OfferPaymentCredit,
                    Amount = monetaryValue,
                    BalanceAfter = walletBalanceAfter,
                    ReferenceType = "OfferTransaction",
                    ReferenceId = transaction.Id,
                    Note = $"Offer payment credited for transaction #{transaction.Id} ({transaction.OfferCode})",
                    RecordedByUserId = userId
                });
                await applicationDbContext.SaveChanges(cancellationToken);
            }

            await dbTransaction.CommitAsync(cancellationToken);

            return new ScanOfferCodeResult(
                offer.Title ?? "Unknown",
                transaction.PointsDeducted,
                transaction.MonetaryValue,
                transaction.CurrencyCode,
                transaction.WalletCreditedAmount);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
