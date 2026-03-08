using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.EmergencyServices.Queries.GetAllEmergencyServices;

public record GetAllEmergencyServicesRequest(bool? IsActive) : IRequest<List<GetAllEmergencyServicesDto>>;

public class GetAllEmergencyServicesRequestHandler(IApplicationDbContext context)
    : IRequestHandler<GetAllEmergencyServicesRequest, List<GetAllEmergencyServicesDto>>
{
    public async Task<List<GetAllEmergencyServicesDto>> Handle(GetAllEmergencyServicesRequest request,
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
            .ToListAsync(cancellationToken);

        return results;
    }
}
