using Application.Common.Extensions;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.AddWalletDeposit;

public record AddWalletDepositResult(decimal NewBalance);

public record AddWalletDepositCommand(
    string ProviderType,
    int ProviderId,
    decimal Amount,
    int TransactionType,
    string? Note
) : IRequest<AddWalletDepositResult>;

public class AddWalletDepositCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<AddWalletDepositCommand, AddWalletDepositResult>
{
    public async Task<AddWalletDepositResult> Handle(AddWalletDepositCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        if (request.ProviderType != "ChargingPoint" && request.ProviderType != "ServiceProvider")
            throw new DataValidationException("ProviderType", "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");

        if (request.TransactionType is (int)WalletTransactionType.SettlementDeduction
            or (int)WalletTransactionType.CommissionDeduction
            or (int)WalletTransactionType.CommissionRefund
            or (int)WalletTransactionType.OfferPaymentCredit
            or (int)WalletTransactionType.OfferPaymentRefund)
            throw new DataValidationException("TransactionType", "SettlementDeduction, CommissionDeduction, CommissionRefund, OfferPaymentCredit and OfferPaymentRefund types are reserved for system use");

        if (request.Amount == 0)
            throw new DataValidationException("Amount", "Amount cannot be zero");

        // Deposit and Refund must be positive; Adjustment can be negative (correction)
        if (request.TransactionType is (int)WalletTransactionType.Deposit or (int)WalletTransactionType.Refund
            && request.Amount < 0)
            throw new DataValidationException("Amount", "Amount must be positive for Deposit and Refund transactions");

        await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            decimal currentBalance;

            if (request.ProviderType == "ChargingPoint")
            {
                var cp = await applicationDbContext.ChargingPoints
                             .FindWithLockAsync("ChargingPoint", request.ProviderId, cancellationToken)
                         ?? throw new NotFoundException($"ChargingPoint with id {request.ProviderId} not found");

                cp.WalletBalance += request.Amount;
                currentBalance = cp.WalletBalance;
            }
            else
            {
                var sp = await applicationDbContext.ServiceProviders
                             .FindWithLockAsync("ServiceProvider", request.ProviderId, cancellationToken)
                         ?? throw new NotFoundException($"ServiceProvider with id {request.ProviderId} not found");

                sp.WalletBalance += request.Amount;
                currentBalance = sp.WalletBalance;
            }

            applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
            {
                ProviderType = request.ProviderType,
                ProviderId = request.ProviderId,
                TransactionType = request.TransactionType,
                Amount = request.Amount,
                BalanceAfter = currentBalance,
                ReferenceType = "Manual",
                ReferenceId = null,
                Note = request.Note,
                RecordedByUserId = userId
            });

            await applicationDbContext.SaveChanges(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            return new AddWalletDepositResult(currentBalance);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
