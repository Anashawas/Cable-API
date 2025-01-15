using Microsoft.EntityFrameworkCore;

namespace Application.PlugTypes.Queries;

public class GetAllPlugTypesRequest() : IRequest<List<GetAllPlugTypesDto>>;

public class GetAllPlugTypesQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllPlugTypesRequest, List<GetAllPlugTypesDto>>
{
    public async Task<List<GetAllPlugTypesDto>> Handle(GetAllPlugTypesRequest request,
        CancellationToken cancellationToken)
        => mapper.Map<List<GetAllPlugTypesDto>>(await applicationDbContext.PlugTypes.AsNoTracking()
            .Where(x => !x.IsDeleted).ToListAsync(cancellationToken));
}