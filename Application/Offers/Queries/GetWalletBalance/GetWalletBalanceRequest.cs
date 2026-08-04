using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetWalletBalance;

public record WalletBalanceDto(
    string ProviderType,
    int ProviderId,
    string? ProviderOwnerName,
    decimal WalletBalance,
    decimal? WalletCreditLimit,
    decimal? AvailableCredit,
    decimal TotalDeposited,
    decimal TotalDeducted
);

public record GetWalletBalanceRequest(
    string ProviderType,
    int ProviderId
) : IRequest<WalletBalanceDto>;

public class GetWalletBalanceRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetWalletBalanceRequest, WalletBalanceDto>
{
    public async Task<WalletBalanceDto> Handle(GetWalletBalanceRequest request,
        CancellationToken cancellationToken)
    {
        string? ownerName = null;
        decimal balance;
        decimal? creditLimit = null;

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .AsNoTracking()
                         .Include(x => x.Owner)
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ChargingPoint with id {request.ProviderId} not found");

            balance = cp.WalletBalance;
            creditLimit = cp.WalletCreditLimit;
            ownerName = cp.Owner?.Name;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .AsNoTracking()
                         .Include(x => x.Owner)
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ServiceProvider with id {request.ProviderId} not found");

            balance = sp.WalletBalance;
            creditLimit = sp.WalletCreditLimit;
            ownerName = sp.Owner?.Name;
        }
        else
        {
            throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        }

        var baseQuery = applicationDbContext.ProviderWalletTransactions
            .AsNoTracking()
            .Where(x => x.ProviderType == request.ProviderType
                         && x.ProviderId == request.ProviderId
                         && !x.IsDeleted);

        var totalDeposited = await baseQuery
            .Where(x => x.Amount > 0)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0;

        var totalDeducted = Math.Abs(await baseQuery
            .Where(x => x.Amount < 0)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0);

        var availableCredit = creditLimit.HasValue
            ? creditLimit.Value + balance
            : (decimal?)null;

        return new WalletBalanceDto(
            request.ProviderType,
            request.ProviderId,
            ownerName,
            balance,
            creditLimit,
            availableCredit,
            totalDeposited,
            totalDeducted
        );
    }
}
