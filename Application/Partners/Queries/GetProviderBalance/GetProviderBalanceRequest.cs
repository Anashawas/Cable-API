using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetProviderBalance;

public record WalletTransactionSummaryDto(
    int Id,
    int TransactionType,
    decimal Amount,
    decimal BalanceAfter,
    string? ReferenceType,
    string? Note,
    string? RecordedByUserName,
    DateTime CreatedAt
);

public record ProviderBalanceDto(
    decimal? WalletCreditLimit,
    decimal WalletBalance,
    decimal? AvailableCredit,
    List<WalletTransactionSummaryDto> RecentTransactions
);

public record GetProviderBalanceRequest(
    string ProviderType,
    int ProviderId
) : IRequest<ProviderBalanceDto>;

public class GetProviderBalanceRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProviderBalanceRequest, ProviderBalanceDto>
{
    public async Task<ProviderBalanceDto> Handle(GetProviderBalanceRequest request, CancellationToken cancellationToken)
    {
        _ = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        decimal? creditLimit;
        decimal walletBalance;

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .AsNoTracking()
                         .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                         .Select(x => new { x.WalletCreditLimit, x.WalletBalance })
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException($"ChargingPoint with Id '{request.ProviderId}' not found");

            creditLimit = cp.WalletCreditLimit;
            walletBalance = cp.WalletBalance;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .AsNoTracking()
                         .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                         .Select(x => new { x.WalletCreditLimit, x.WalletBalance })
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException($"ServiceProvider with Id '{request.ProviderId}' not found");

            creditLimit = sp.WalletCreditLimit;
            walletBalance = sp.WalletBalance;
        }
        else
        {
            throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        }

        var recentTransactions = await applicationDbContext.ProviderWalletTransactions
            .AsNoTracking()
            .Where(t => t.ProviderType == request.ProviderType
                        && t.ProviderId == request.ProviderId
                        && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .Take(10)
            .Select(t => new WalletTransactionSummaryDto(
                t.Id,
                t.TransactionType,
                t.Amount,
                t.BalanceAfter,
                t.ReferenceType,
                t.Note,
                t.RecordedByUser != null ? t.RecordedByUser.Name : null,
                t.CreatedAt))
            .ToListAsync(cancellationToken);

        var availableCredit = creditLimit.HasValue
            ? creditLimit.Value + walletBalance
            : (decimal?)null;

        return new ProviderBalanceDto(creditLimit, walletBalance, availableCredit, recentTransactions);
    }
}
