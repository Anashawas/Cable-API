using Application.Common.Models;
using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.EmergencyServices.Queries.GetAllEmergencyServices;

public record GetAllEmergencyServicesRequest(bool? IsActive, int? Page = null, int? PageSize = null) : IRequest<PagedResult<GetAllEmergencyServicesDto>>;

public class GetAllEmergencyServicesRequestHandler(IApplicationDbContext context)
    : IRequestHandler<GetAllEmergencyServicesRequest, PagedResult<GetAllEmergencyServicesDto>>
{
    public async Task<PagedResult<GetAllEmergencyServicesDto>> Handle(GetAllEmergencyServicesRequest request,
        CancellationToken cancellationToken)
    {
        var query = context.EmergencyServices
            .Where(x => !x.IsDeleted);

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.IsActive == request.IsActive.Value);
        }

        var results = await query
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Title)
            .Select(x => new GetAllEmergencyServicesDto(
                x.Id,
                x.Title,
                x.Description,
                x.ImageUrl,
                x.SubscriptionType,
                x.PriceDetails,
                x.ActionUrl,
                x.OpenFrom,
                x.OpenTo,
                x.PhoneNumber,
                x.WhatsAppNumber,
                x.IsActive,
                x.SortOrder
            ))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);

        return results;
    }
}
