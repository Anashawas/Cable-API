using Application.Common.Models;
using Application.Offers.Queries.GetSettlements;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetSettlementsPaged;

/// <summary>
/// C6 — server-side paged settlements list. Paging opt-in (omit page/pageSize
/// for the full filtered set in the envelope). Rows include the provider's
/// current wallet balance (batched — no per-row balance calls needed).
/// </summary>
public record GetSettlementsPagedRequest(
    int? Status = null, int? Month = null, int? Year = null,
    int? PeriodType = null, int? Week = null,
    string? Search = null, int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<ProviderSettlementDto>>;

public class GetSettlementsPagedRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetSettlementsPagedRequest, PagedResult<ProviderSettlementDto>>
{
    public async Task<PagedResult<ProviderSettlementDto>> Handle(GetSettlementsPagedRequest request,
        CancellationToken cancellationToken)
    {
        var query = SettlementQueryHelper.ApplyFilters(
            applicationDbContext, request.Status, request.Month, request.Year,
            request.PeriodType, request.Week, request.Search);

        var paged = await query
            .OrderByDescending(x => x.PeriodYear)
            .ThenByDescending(x => x.PeriodMonth)
            .ThenByDescending(x => x.PeriodWeek)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        var items = await SettlementQueryHelper.MapAsync(applicationDbContext, paged.Items, cancellationToken);
        return paged.As(items);
    }
}
