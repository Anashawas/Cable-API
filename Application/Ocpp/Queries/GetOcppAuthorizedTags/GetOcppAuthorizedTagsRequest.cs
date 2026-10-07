using Application.Common.Interfaces;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetOcppAuthorizedTags;

/// <summary>Admin or station owner/manager: the cards / passwords allowed to charge at a station.</summary>
public record GetOcppAuthorizedTagsRequest(int ChargingPointId, bool IncludeDisabled = true) : IRequest<List<OcppAuthorizedTagDto>>;

public class GetOcppAuthorizedTagsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetOcppAuthorizedTagsRequest, List<OcppAuthorizedTagDto>>
{
    public async Task<List<OcppAuthorizedTagDto>> Handle(GetOcppAuthorizedTagsRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser,
            ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken);

        var query = db.OcppAuthorizedTags.AsNoTracking()
            .Where(t => t.ChargingPointId == request.ChargingPointId && !t.IsDeleted);
        if (!request.IncludeDisabled)
            query = query.Where(t => t.IsEnabled);

        return await query
            .OrderBy(t => t.Label ?? t.IdTag)
            .Select(t => new OcppAuthorizedTagDto(t.Id, t.ChargingPointId, t.IdTag, t.Label, t.IsEnabled, t.ExpiresAt, t.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
