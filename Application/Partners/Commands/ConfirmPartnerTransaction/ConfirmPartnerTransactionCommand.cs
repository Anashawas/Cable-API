using Application.Common.Extensions;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Commands.ConfirmPartnerTransaction;

public record ScanPartnerCodeResult(
    string ProviderName,
    decimal TransactionAmount,
    string CurrencyCode,
    decimal CommissionAmount,
    int PointsAwarded
);

public record ScanPartnerCodeCommand(string TransactionCode) : IRequest<ScanPartnerCodeResult>;

public class ScanPartnerCodeCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    ILoyaltyPointService loyaltyPointService,
    ISettlementService settlementService)
    : IRequestHandler<ScanPartnerCodeCommand, ScanPartnerCodeResult>
{
    public async Task<ScanPartnerCodeResult> Handle(ScanPartnerCodeCommand request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var transaction = await applicationDbContext.PartnerTransactions
                              .Include(x => x.Agreement)
                              .FirstOrDefaultAsync(x => x.TransactionCode == request.TransactionCode
                                                        && !x.IsDeleted
                                                        && x.Status == (int)PartnerTransactionStatus.Initiated,
                                  cancellationToken)
                          ?? throw new NotFoundException($"Initiated partner transaction with code '{request.TransactionCode}' not found");

        // Check if code has expired
        if (DateTime.UtcNow > transaction.CodeExpiresAt)
        {
            await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                transaction.Status = (int)PartnerTransactionStatus.Expired;

                // Refund reserved commission to wallet balance
                if (transaction.CommissionAmount is > 0)
                {
                    if (transaction.ProviderType == "ChargingPoint")
                    {
                        var cp = await applicationDbContext.ChargingPoints
                            .FindWithLockAsync("ChargingPoint", transaction.ProviderId, cancellationToken);
                        if (cp != null)
                        {
                            cp.WalletBalance += transaction.CommissionAmount.Value;
                            applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                            {
                                ProviderType = transaction.ProviderType,
                                ProviderId = transaction.ProviderId,
                                TransactionType = (int)WalletTransactionType.CommissionRefund,
                                Amount = transaction.CommissionAmount.Value,
                                BalanceAfter = cp.WalletBalance,
                                ReferenceType = "PartnerTransaction",
                                ReferenceId = transaction.Id,
                                Note = $"Commission refunded - transaction code expired ({transaction.TransactionCode})",
                                RecordedByUserId = userId
                            });
                        }
                    }
                    else if (transaction.ProviderType == "ServiceProvider")
                    {
                        var sp = await applicationDbContext.ServiceProviders
                            .FindWithLockAsync("ServiceProvider", transaction.ProviderId, cancellationToken);
                        if (sp != null)
                        {
                            sp.WalletBalance += transaction.CommissionAmount.Value;
                            applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                            {
                                ProviderType = transaction.ProviderType,
                                ProviderId = transaction.ProviderId,
                                TransactionType = (int)WalletTransactionType.CommissionRefund,
                                Amount = transaction.CommissionAmount.Value,
                                BalanceAfter = sp.WalletBalance,
                                ReferenceType = "PartnerTransaction",
                                ReferenceId = transaction.Id,
                                Note = $"Commission refunded - transaction code expired ({transaction.TransactionCode})",
                                RecordedByUserId = userId
                            });
                        }
                    }
                }

                await applicationDbContext.SaveChanges(cancellationToken);
                await dbTransaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await dbTransaction.RollbackAsync(cancellationToken);
                throw;
            }
            throw new DataValidationException("TransactionCode", "This transaction code has expired");
        }

        // Wrap completion + points award in a transaction
        await using var completionTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Set the user who scanned and complete the transaction
            transaction.UserId = userId;
            transaction.CompletedAt = DateTime.UtcNow;
            transaction.Status = (int)PartnerTransactionStatus.Completed;

            // Auto-update settlement for this transaction
            await settlementService.UpsertSettlementForPartnerTransactionAsync(transaction, cancellationToken);

            await applicationDbContext.SaveChanges(cancellationToken);

            // Award loyalty points inside transaction (rolls back if fails)
            var pointsAwarded = transaction.PointsAwarded ?? 0;
            if (pointsAwarded > 0)
            {
                await loyaltyPointService.AwardPointsFromOfferAsync(
                    userId,
                    pointsAwarded,
                    transaction.ProviderType,
                    transaction.ProviderId,
                    transaction.Id,
                    note: "Partner transaction points",
                    cancellationToken: cancellationToken);
            }

            await completionTransaction.CommitAsync(cancellationToken);

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

            return new ScanPartnerCodeResult(
                providerName ?? "Unknown",
                transaction.TransactionAmount ?? 0,
                transaction.CurrencyCode ?? "",
                transaction.CommissionAmount ?? 0,
                pointsAwarded);
        }
        catch
        {
            await completionTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
