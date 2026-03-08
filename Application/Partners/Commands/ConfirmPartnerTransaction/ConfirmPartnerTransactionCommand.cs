using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
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
    ILoyaltyPointService loyaltyPointService)
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
            transaction.Status = (int)PartnerTransactionStatus.Expired;

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
            throw new DataValidationException("TransactionCode", "This transaction code has expired");
        }

        // Set the user who scanned and complete the transaction
        transaction.UserId = userId;
        transaction.CompletedAt = DateTime.UtcNow;
        transaction.Status = (int)PartnerTransactionStatus.Completed;

        await applicationDbContext.SaveChanges(cancellationToken);

        // Award loyalty points
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

        // Resolve provider name for the response
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
}
