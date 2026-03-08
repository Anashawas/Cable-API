using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
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

        transaction.Status = (int)PartnerTransactionStatus.Cancelled;
        transaction.ModifiedAt = DateTime.UtcNow;
        transaction.ModifiedBy = userId;

        // Refund reserved commission to provider balance
        if (transaction.CommissionAmount is > 0)
        {
            if (transaction.ProviderType == "ChargingPoint")
            {
                var cp = await applicationDbContext.ChargingPoints
                    .FirstOrDefaultAsync(x => x.Id == transaction.ProviderId && !x.IsDeleted, cancellationToken);
                if (cp != null) cp.LoyaltyCurrentBalance += transaction.CommissionAmount.Value;
            }
            else if (transaction.ProviderType == "ServiceProvider")
            {
                var sp = await applicationDbContext.ServiceProviders
                    .FirstOrDefaultAsync(x => x.Id == transaction.ProviderId && !x.IsDeleted, cancellationToken);
                if (sp != null) sp.LoyaltyCurrentBalance += transaction.CommissionAmount.Value;
            }
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
