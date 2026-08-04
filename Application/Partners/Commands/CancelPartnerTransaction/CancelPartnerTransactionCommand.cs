using Application.Common.Extensions;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Commands.CancelPartnerTransaction;

public record CancelPartnerTransactionCommand(int Id) : IRequest;

public class CancelPartnerTransactionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CancelPartnerTransactionCommand>
{
    public async Task Handle(CancelPartnerTransactionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var transaction = await applicationDbContext.PartnerTransactions
                              .FirstOrDefaultAsync(x => x.Id == request.Id
                                                        && x.ConfirmedByUserId == userId
                                                        && !x.IsDeleted
                                                        && x.Status == (int)PartnerTransactionStatus.Initiated,
                                  cancellationToken)
                          ?? throw new NotFoundException($"Initiated partner transaction with Id '{request.Id}' not found");

        await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            transaction.Status = (int)PartnerTransactionStatus.Cancelled;
            transaction.ModifiedAt = DateTime.UtcNow;
            transaction.ModifiedBy = userId;

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
                            Note = $"Commission refunded - transaction cancelled ({transaction.TransactionCode})",
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
                            Note = $"Commission refunded - transaction cancelled ({transaction.TransactionCode})",
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
    }
}
