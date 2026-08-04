using Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetActivePartners;

public record PartnerDto(
    int Id,
    string ProviderType,
    int ProviderId,
    string? ProviderName,
    double CommissionPercentage,
    double PointsRewardPercentage,
    int CodeExpirySeconds,
    decimal? MinimumTransactionAmount,
    string? Note
);

public record GetActivePartnersRequest(string? ProviderType, int? Page = null, int? PageSize = null) : IRequest<PagedResult<PartnerDto>>;

public class GetActivePartnersRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetActivePartnersRequest, PagedResult<PartnerDto>>
{
    public async Task<PagedResult<PartnerDto>> Handle(GetActivePartnersRequest request, CancellationToken cancellationToken)
    {
        var query = applicationDbContext.PartnerAgreements
            .Where(x => x.IsActive && !x.IsDeleted);

        if (!string.IsNullOrEmpty(request.ProviderType))
            query = query.Where(x => x.ProviderType == request.ProviderType);

        var paged = await query.OrderByDescending(x => x.CreatedAt)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
        var agreements = paged.Items;

        // Batch load provider names (avoids N+1 queries)
        var cpIds = agreements.Where(a => a.ProviderType == "ChargingPoint").Select(a => a.ProviderId).Distinct().ToList();
        var spIds = agreements.Where(a => a.ProviderType == "ServiceProvider").Select(a => a.ProviderId).Distinct().ToList();

        var cpNames = cpIds.Count > 0
            ? await applicationDbContext.ChargingPoints.Where(x => cpIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();
        var spNames = spIds.Count > 0
            ? await applicationDbContext.ServiceProviders.Where(x => spIds.Contains(x.Id) && !x.IsDeleted).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken)
            : new Dictionary<int, string>();

        return paged.As(agreements.Select(a => new PartnerDto(
            a.Id, a.ProviderType, a.ProviderId,
            a.ProviderType == "ChargingPoint" ? cpNames.GetValueOrDefault(a.ProviderId) : spNames.GetValueOrDefault(a.ProviderId),
            a.CommissionPercentage, a.PointsRewardPercentage,
            a.CodeExpirySeconds, a.MinimumTransactionAmount, a.Note)).ToList());
    }
}
