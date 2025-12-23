using Microsoft.EntityFrameworkCore;

namespace Application.SystemVersions.Queries.GetAllSystemVersions;

public record GetAllSystemVersionsRequest : IRequest<List<GetAllSystemVersionsDto>>;

public record GetAllSystemVersionsDto(int Id, string Platform, string Version, bool UpdateForce);

public class GetAllSystemVersionsQueryHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllSystemVersionsRequest, List<GetAllSystemVersionsDto>>
{
    public async Task<List<GetAllSystemVersionsDto>> Handle(GetAllSystemVersionsRequest request,
        CancellationToken cancellationToken)
        => await applicationDbContext.SystemVersions.AsNoTracking()
            .Select(x => new GetAllSystemVersionsDto(x.Id, x.Platform, x.Version, x.ForceUpdate))
            .ToListAsync(cancellationToken);
}