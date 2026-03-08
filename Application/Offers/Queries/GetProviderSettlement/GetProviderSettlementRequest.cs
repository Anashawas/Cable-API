using Application.Offers.Queries.GetSettlements;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetProviderSettlement;

public record GetProviderSettlementRequest(string ProviderType, int ProviderId, int Year, int Month)
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
                                              && s.PeriodYear == request.Year
                                              && s.PeriodMonth == request.Month
                                              && !s.IsDeleted, cancellationToken)
                ?? throw new NotFoundException(
                    $"Settlement for {request.ProviderType}/{request.ProviderId} in {request.Year}-{request.Month} not found");

        return new ProviderSettlementDto(
            x.Id, x.ProviderType, x.ProviderId,
            x.ProviderOwnerId, x.ProviderOwner?.Name,
            x.PeriodYear, x.PeriodMonth,
            x.PartnerTransactionCount, x.PartnerTransactionAmount,
            x.PartnerCommissionAmount, x.TotalPointsAwarded,
            x.OfferTransactionCount, x.OfferPaymentAmount,
            x.TotalPointsDeducted, x.NetAmountDueToProvider,
            x.SettlementStatus, x.InvoicedAt, x.PaidAt,
            x.PaidAmount, x.AdminNote, x.CreatedAt
        );
    }
}
