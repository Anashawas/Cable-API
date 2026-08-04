using Application.Common.Interfaces.Repositories;
using Application.Common.Models;

namespace Application.ChargingPoints.Queries.GetChargingPointsPaged;

/// <summary>
/// A2b — server-side paged + filtered stations list for the admin portal.
/// Paging is opt-in: when page/pageSize are omitted all matching rows are
/// returned (still in the paged envelope). Rows reuse GetAllChargingPointsDto.
/// </summary>
public record GetChargingPointsPagedRequest(
    string? Search = null,
    int? ChargerPointTypeId = null,
    string? CityName = null,
    int? StatusId = null,
    int? ChargerBrandId = null,
    bool? IsVerified = null,
    int? PlugTypeId = null,
    string? Sort = null,   // name_asc|name_desc|visitors_asc|visitors_desc|rating_asc|rating_desc
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<GetAllChargingPointsDto>>;

public class GetChargingPointsPagedRequestHandler(
    IChargingPointRepository chargingPointRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetChargingPointsPagedRequest, PagedResult<GetAllChargingPointsDto>>
{
    public async Task<PagedResult<GetAllChargingPointsDto>> Handle(GetChargingPointsPagedRequest request,
        CancellationToken cancellationToken)
        => await chargingPointRepository.GetChargingPointsPaged(
            request, currentUserService.UserId, cancellationToken);
}
