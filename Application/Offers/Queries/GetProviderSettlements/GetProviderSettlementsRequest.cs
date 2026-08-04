using Application.Common.Models;
using Application.Offers.Queries.GetSettlements;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetProviderSettlements;

public record GetProviderSettlementsRequest(
    string ProviderType,
    int ProviderId,
    int? Status = null,
    int? Year = null,
    int? Week = null,
    bool UnpaidOnly = false,
    bool HasDebt = false, // Paid settlements where provider didn't pay full amount (CommissionAmount - WalletApplied > 0)
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<ProviderSettlementDto>>;

public class GetProviderSettlementsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderSettlementsRequest, PagedResult<ProviderSettlementDto>>
{
    public async Task<PagedResult<ProviderSettlementDto>> Handle(GetProviderSettlementsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProviderType != "ChargingPoint" && request.ProviderType != "ServiceProvider")
            throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");

        var query = applicationDbContext.ProviderSettlements
            .AsNoTracking()
            .Include(x => x.ProviderOwner)
            .Where(x => x.ProviderType == request.ProviderType
                         && x.ProviderId == request.ProviderId
                         && !x.IsDeleted);

        if (request.Status.HasValue)
            query = query.Where(x => x.SettlementStatus == request.Status.Value);

        if (request.Year.HasValue)
            query = query.Where(x => x.PeriodYear == request.Year.Value);

        if (request.Week.HasValue)
            query = query.Where(x => x.PeriodWeek == request.Week.Value);

        // UnpaidOnly: show settlements that are NOT Paid (Pending or Disputed)
        if (request.UnpaidOnly)
            query = query.Where(x => x.SettlementStatus != (int)SettlementStatus.Paid);

        // HasDebt: Paid settlements where provider didn't pay full amount from wallet
        if (request.HasDebt)
            query = query.Where(x => x.SettlementStatus == (int)SettlementStatus.Paid
                                     && x.PartnerCommissionAmount - x.WalletApplied > 0);

        var paged = await query
            .OrderByDescending(x => x.PeriodYear)
            .ThenByDescending(x => x.PeriodWeek)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
        var settlements = paged.Items;

        // Load provider details
        string? providerName = null, providerPhone = null, providerAddress = null, providerIcon = null;

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken);
            if (cp != null)
            {
                providerName = cp.Name;
                providerPhone = cp.Phone;
                providerAddress = cp.Address;
                providerIcon = cp.Icon;
            }
        }
        else
        {
            var sp = await applicationDbContext.ServiceProviders.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken);
            if (sp != null)
            {
                providerName = sp.Name;
                providerPhone = sp.Phone;
                providerAddress = sp.Address;
                providerIcon = sp.Icon;
            }
        }

        var providerDetails = new ProviderDetailsDto(
            request.ProviderType, request.ProviderId,
            providerName, providerPhone, providerAddress, providerIcon);

        return paged.As(settlements.Select(x => new ProviderSettlementDto(
            x.Id,
            providerDetails,
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
        )).ToList());
    }
}
