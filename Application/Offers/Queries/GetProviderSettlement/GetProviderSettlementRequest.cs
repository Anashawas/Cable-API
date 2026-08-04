using Application.Offers.Queries.GetSettlements;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetProviderSettlement;

public record GetProviderSettlementRequest(
    string ProviderType, int ProviderId, int Year, int Month,
    int PeriodType = 1, int Week = 0)
    : IRequest<ProviderSettlementDto>;

public class GetProviderSettlementRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderSettlementRequest, ProviderSettlementDto>
{
    public async Task<ProviderSettlementDto> Handle(GetProviderSettlementRequest request,
        CancellationToken cancellationToken)
    {
        var x = await applicationDbContext.ProviderSettlements
                    .AsNoTracking()
                    .Include(s => s.ProviderOwner)
                    .FirstOrDefaultAsync(s => s.ProviderType == request.ProviderType
                                              && s.ProviderId == request.ProviderId
                                              && s.PeriodType == request.PeriodType
                                              && s.PeriodYear == request.Year
                                              && s.PeriodMonth == request.Month
                                              && s.PeriodWeek == request.Week
                                              && !s.IsDeleted, cancellationToken)
                ?? throw new NotFoundException(
                    $"Settlement for {request.ProviderType}/{request.ProviderId} in {request.Year}-{request.Month} not found");

        // Load provider details
        string? providerName = null, providerPhone = null, providerAddress = null, providerIcon = null;

        if (x.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == x.ProviderId && !c.IsDeleted, cancellationToken);
            if (cp != null)
            {
                providerName = cp.Name;
                providerPhone = cp.Phone;
                providerAddress = cp.Address;
                providerIcon = cp.Icon;
            }
        }
        else if (x.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == x.ProviderId && !s.IsDeleted, cancellationToken);
            if (sp != null)
            {
                providerName = sp.Name;
                providerPhone = sp.Phone;
                providerAddress = sp.Address;
                providerIcon = sp.Icon;
            }
        }

        return new ProviderSettlementDto(
            x.Id,
            new ProviderDetailsDto(x.ProviderType, x.ProviderId, providerName, providerPhone, providerAddress, providerIcon),
            new OwnerDetailsDto(x.ProviderOwnerId, x.ProviderOwner?.Name, x.ProviderOwner?.Email, x.ProviderOwner?.Phone),
            x.PeriodYear, x.PeriodMonth,
            x.PeriodType, x.PeriodWeek,
            new PartnerTransactionSummaryDto(x.PartnerTransactionCount, x.PartnerTransactionAmount, x.PartnerCommissionAmount, x.TotalPointsAwarded),
            new OfferTransactionSummaryDto(x.OfferTransactionCount, x.OfferPaymentAmount, x.TotalPointsDeducted),
            x.NetBalance,
            x.WalletApplied,
            x.PartnerCommissionAmount - x.WalletApplied, // OutstandingAmount
            x.SettlementStatus, x.PaidAt,
            x.AdminNote, x.CreatedAt
        );
    }
}
