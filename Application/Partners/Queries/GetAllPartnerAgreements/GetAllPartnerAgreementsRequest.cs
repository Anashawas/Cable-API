using Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetAllPartnerAgreements;

public record AdminPartnerAgreementDto(
    int Id,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    double CommissionPercentage,
    double PointsRewardPercentage,
    int? PointsConversionRateId,
    string? ConversionRateName,
    int CodeExpirySeconds,
    decimal? MinimumTransactionAmount,
    bool IsActive,
    string? Note,
    DateTime? CreatedAt
);

public record GetAllPartnerAgreementsRequest(bool? IsActive, int? Page = null, int? PageSize = null) : IRequest<PagedResult<AdminPartnerAgreementDto>>;

public class GetAllPartnerAgreementsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllPartnerAgreementsRequest, PagedResult<AdminPartnerAgreementDto>>
{
    public async Task<PagedResult<AdminPartnerAgreementDto>> Handle(
        GetAllPartnerAgreementsRequest request, CancellationToken cancellationToken)
    {
        var query = applicationDbContext.PartnerAgreements
            .Include(x => x.ConversionRate)
            .Where(x => !x.IsDeleted);

        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        var paged = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
        var agreements = paged.Items;

        // Batch load provider names (avoids N+1 queries)
        var cpIds = agreements.Where(a => a.ProviderType == "ChargingPoint").Select(a => a.ProviderId).Distinct().ToList();
        var spIds = agreements.Where(a => a.ProviderType == "ServiceProvider").Select(a => a.ProviderId).Distinct().ToList();

        var cpNames = cpIds.Count > 0
            ? await applicationDbContext.ChargingPoints.Where(x => cpIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();
        var spNames = spIds.Count > 0
            ? await applicationDbContext.ServiceProviders.Where(x => spIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();

        return paged.As(agreements.Select(a => new AdminPartnerAgreementDto(
            a.Id, a.ProviderType, a.ProviderId,
            a.ProviderType == "ChargingPoint" ? cpNames.GetValueOrDefault(a.ProviderId) : spNames.GetValueOrDefault(a.ProviderId),
            a.CommissionPercentage, a.PointsRewardPercentage,
            a.PointsConversionRateId, a.ConversionRate?.Name,
            a.CodeExpirySeconds, a.MinimumTransactionAmount,
            a.IsActive, a.Note, a.CreatedAt)).ToList());
    }
}
