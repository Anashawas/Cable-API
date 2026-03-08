using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetProviderBalance;

public record ProviderPaymentDto(
    int Id,
    decimal Amount,
    string? Note,
    string? RecordedByUserName,
    DateTime CreatedAt
);

public record ProviderBalanceDto(
    decimal? CreditLimit,
    decimal CurrentBalance,
    decimal? AvailableCredit,
    List<ProviderPaymentDto> RecentPayments
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
        decimal currentBalance;

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .AsNoTracking()
                         .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                         .Select(x => new { x.LoyaltyCreditLimit, x.LoyaltyCurrentBalance })
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException($"ChargingPoint with Id '{request.ProviderId}' not found");

            creditLimit = cp.LoyaltyCreditLimit;
            currentBalance = cp.LoyaltyCurrentBalance;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .AsNoTracking()
                         .Where(x => x.Id == request.ProviderId && !x.IsDeleted)
                         .Select(x => new { x.LoyaltyCreditLimit, x.LoyaltyCurrentBalance })
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException($"ServiceProvider with Id '{request.ProviderId}' not found");

            creditLimit = sp.LoyaltyCreditLimit;
            currentBalance = sp.LoyaltyCurrentBalance;
        }
        else
        {
            throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        }

        var recentPayments = await applicationDbContext.ProviderPayments
            .AsNoTracking()
            .Where(p => p.ProviderType == request.ProviderType
                        && p.ProviderId == request.ProviderId
                        && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .Select(p => new ProviderPaymentDto(
                p.Id,
                p.Amount,
                p.Note,
                p.RecordedByUser.Name,
                p.CreatedAt))
            .ToListAsync(cancellationToken);

        var availableCredit = creditLimit.HasValue
            ? creditLimit.Value + currentBalance
            : (decimal?)null;

        return new ProviderBalanceDto(creditLimit, currentBalance, availableCredit, recentPayments);
    }
}
