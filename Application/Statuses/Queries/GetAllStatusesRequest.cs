using Microsoft.EntityFrameworkCore;

namespace Application.Statuses.Queries;

public class GetAllStatusesRequest : IRequest<List<GetAllStatusesDto>>;


public class GetAllStatusesQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllStatusesRequest, List<GetAllStatusesDto>>
{
    public async Task<List<GetAllStatusesDto>> Handle(GetAllStatusesRequest request,
        CancellationToken cancellationToken)
        => mapper.Map<List<GetAllStatusesDto>>(await applicationDbContext.Statuses.AsNoTracking()
                .ToListAsync(cancellationToken));
}
